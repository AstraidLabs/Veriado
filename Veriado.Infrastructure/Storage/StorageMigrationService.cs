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
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Entities;

namespace Veriado.Infrastructure.Storage;

/// <summary>
/// Implements storage root migration with validation, atomic writes, and verification.
/// </summary>
public sealed class StorageMigrationService : IStorageMigrationService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IFilePathResolver _pathResolver;
    private readonly IFileHashCalculator _hashCalculator;
    private readonly IOperationalPauseCoordinator _pauseCoordinator;
    private readonly ILogger<StorageMigrationService> _logger;

    public StorageMigrationService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IFilePathResolver pathResolver,
        IFileHashCalculator hashCalculator,
        IOperationalPauseCoordinator pauseCoordinator,
        ILogger<StorageMigrationService> logger)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _hashCalculator = hashCalculator ?? throw new ArgumentNullException(nameof(hashCalculator));
        _pauseCoordinator = pauseCoordinator ?? throw new ArgumentNullException(nameof(pauseCoordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<StorageMigrationResult> MigrateStorageRootAsync(
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
                return new StorageMigrationResult(normalizedOldRoot, normalizedTargetRoot);
            }

            var files = await dbContext.FileSystems
                .AsNoTracking()
                .Select(f => new { f.RelativePath, f.Size, f.Hash })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var errors = new List<string>();
            var migrated = 0;
            var missing = 0;
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
                    if (info.Length != file.Size.Value)
                    {
                        verificationFailures++;
                        errors.Add($"Size mismatch for {relativePath}: expected {file.Size.Value} bytes, found {info.Length} bytes.");
                        continue;
                    }

                    if (options.VerifyHashes)
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

            return new StorageMigrationResult(normalizedOldRoot, normalizedTargetRoot)
            {
                MigratedFiles = migrated,
                MissingSources = missing,
                VerificationFailures = verificationFailures,
                Errors = errors,
            };
        }
        finally
        {
            _pauseCoordinator.Resume();
        }
    }
}
