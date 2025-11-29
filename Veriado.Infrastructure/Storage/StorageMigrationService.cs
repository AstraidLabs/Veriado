using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Veriado.Appl.Abstractions;
using Veriado.Application.Abstractions;
using Veriado.Infrastructure.FileSystem;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Entities;

namespace Veriado.Infrastructure.Storage;

/// <summary>
/// Implements storage root migration with validation, atomic writes, and verification.
/// </summary>
public sealed class StorageMigrationService : IStorageMigrationService
{
    private const double SafetyMargin = 1.1d;

    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IFilePathResolver _pathResolver;
    private readonly IFileHashCalculator _hashCalculator;
    private readonly IOperationalPauseCoordinator _pauseCoordinator;
    private readonly IStorageSpaceAnalyzer _spaceAnalyzer;
    private readonly ILogger<StorageMigrationService> _logger;

    public StorageMigrationService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IFilePathResolver pathResolver,
        IFileHashCalculator hashCalculator,
        IOperationalPauseCoordinator pauseCoordinator,
        IStorageSpaceAnalyzer spaceAnalyzer,
        ILogger<StorageMigrationService> logger)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _hashCalculator = hashCalculator ?? throw new ArgumentNullException(nameof(hashCalculator));
        _pauseCoordinator = pauseCoordinator ?? throw new ArgumentNullException(nameof(pauseCoordinator));
        _spaceAnalyzer = spaceAnalyzer ?? throw new ArgumentNullException(nameof(spaceAnalyzer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<StorageOperationResult> MigrateStorageRootAsync(
        string newRootPath,
        StorageMigrationOptions? options,
        CancellationToken cancellationToken)
    {
        options ??= new StorageMigrationOptions();
        var normalizedTargetRoot = SafePathUtilities.NormalizeAndValidateRoot(newRootPath, _logger);

        await _pauseCoordinator.PauseAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var storageRoot = await dbContext.StorageRoots
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("Storage root is not configured.");

            var normalizedOldRoot = SafePathUtilities.NormalizeAndValidateRoot(storageRoot.RootPath, _logger);
            if (SafePathUtilities.ArePathsEquivalent(normalizedOldRoot, normalizedTargetRoot))
            {
                _logger.LogInformation("Requested storage root matches the current value; skipping migration.");
                return new StorageOperationResult
                {
                    Status = StorageOperationStatus.Success,
                    TargetStorageRoot = normalizedTargetRoot,
                    MissingFilesCount = 0,
                    FailedFilesCount = 0,
                    WarningCount = 0,
                    Warnings = Array.Empty<string>(),
                };
            }

            var files = await dbContext.FileSystems
                .AsNoTracking()
                .Select(f => new { f.RelativePath, f.Size, f.Hash })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var totalSize = files.Sum(f => f.Size.Value);
            var available = await _spaceAnalyzer.GetAvailableBytesAsync(normalizedTargetRoot, cancellationToken).ConfigureAwait(false);
            var required = (long)Math.Ceiling(totalSize * SafetyMargin);
            if (available < required)
            {
                return new StorageOperationResult
                {
                    Status = StorageOperationStatus.InsufficientSpace,
                    Message = $"Insufficient space for migration. Required {required} bytes, available {available} bytes.",
                    RequiredBytes = required,
                    AvailableBytes = available,
                    TargetStorageRoot = normalizedTargetRoot,
                    MissingFilesCount = 0,
                    FailedFilesCount = 0,
                    WarningCount = 0,
                    Warnings = Array.Empty<string>(),
                };
            }

            var errors = new List<string>();
            var migrated = 0;
            var missing = 0;
            var missingPaths = new List<string>();
            var verificationFailures = 0;

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = SafePathUtilities.NormalizeRelative(file.RelativePath.Value, _logger);
                var sourcePath = Path.Combine(normalizedOldRoot, relativePath);
                var destinationPath = Path.Combine(normalizedTargetRoot, relativePath);

                try
                {
                    await AtomicFileOperations.CopyAsync(sourcePath, destinationPath, overwrite: true, cancellationToken)
                        .ConfigureAwait(false);
                    migrated++;

                    if (options.DeleteSourceAfterCopy)
                    {
                        AtomicFileOperations.TryDelete(sourcePath, _logger);
                    }
                }
                catch (FileNotFoundException)
                {
                    missing++;
                    missingPaths.Add(relativePath);
                    _logger.LogWarning("Source file {SourcePath} missing during migration.", sourcePath);
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to migrate {relativePath}: {ex.Message}");
                    _logger.LogError(ex, "Failed to migrate {RelativePath}.", relativePath);
                }
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = SafePathUtilities.NormalizeRelative(file.RelativePath.Value, _logger);
                var destinationPath = Path.Combine(normalizedTargetRoot, relativePath);

                try
                {
                    if (!File.Exists(destinationPath))
                    {
                        verificationFailures++;
                        errors.Add($"Missing migrated file {relativePath}.");
                        continue;
                    }

                    var info = new FileInfo(destinationPath);
                    if (options.Verification.VerifyFilesBySize && info.Length != file.Size.Value)
                    {
                        verificationFailures++;
                        errors.Add($"Size mismatch for {relativePath}: expected {file.Size.Value} bytes, found {info.Length} bytes.");
                        continue;
                    }

                    if (options.Verification.VerifyFilesByHash && !string.IsNullOrWhiteSpace(file.Hash.Value))
                    {
                        var computedHash = await _hashCalculator
                            .ComputeSha256Async(destinationPath, cancellationToken)
                            .ConfigureAwait(false);

                        if (!computedHash.Value.Equals(file.Hash.Value, StringComparison.OrdinalIgnoreCase))
                        {
                            verificationFailures++;
                            errors.Add($"Hash mismatch for {relativePath}.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    verificationFailures++;
                    errors.Add($"Verification failed for {relativePath}: {ex.Message}");
                    _logger.LogError(ex, "Verification failed for {RelativePath}.", relativePath);
                }
            }

            if (errors.Count == 0 && missing == 0 && verificationFailures == 0)
            {
                storageRoot.UpdateRootPath(normalizedTargetRoot);
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                if (_pathResolver is FilePathResolver resolver)
                {
                    resolver.OverrideCachedRoot(normalizedTargetRoot);
                }

                _logger.LogInformation(
                    "Storage migration completed successfully. Updated storage root to {NewRoot}.",
                    normalizedTargetRoot);
            }
            else
            {
                _logger.LogError(
                    "Storage migration completed with errors. Missing={Missing}, VerificationFailures={VerificationFailures}, ErrorCount={Errors}.",
                    missing,
                    verificationFailures,
                    errors.Count);
            }

            var status = errors.Count == 0 && missing == 0 && verificationFailures == 0
                ? StorageOperationStatus.Success
                : StorageOperationStatus.PartialSuccess;

            return new StorageOperationResult
            {
                Status = status,
                TargetStorageRoot = normalizedTargetRoot,
                AffectedFiles = migrated,
                MissingFiles = missingPaths,
                MissingFilesCount = missingPaths.Count,
                FailedFilesCount = errors.Count + verificationFailures,
                WarningCount = missingPaths.Count + verificationFailures + errors.Count,
                Warnings = missingPaths,
                FailedVerificationCount = verificationFailures,
                VerifiedFilesCount = files.Count - missing,
                Errors = errors,
                Message = status == StorageOperationStatus.Success ? "Migration completed." : "Migration completed with warnings.",
            };
        }
        finally
        {
            _pauseCoordinator.Resume();
        }
    }
}
