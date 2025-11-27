using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Veriado.Appl.Abstractions;
using Veriado.Application.Abstractions;
using Veriado.Infrastructure.FileSystem;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Connections;
using Veriado.Infrastructure.Persistence.Entities;

namespace Veriado.Infrastructure.Storage;

public sealed class ImportPackageService : IImportPackageService
{
    private const string MetadataFileName = "metadata.json";
    private const string DatabaseDirectory = "db";
    private const string StorageDirectory = "storage";
    private const double SafetyMargin = 1.1d;

    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IConnectionStringProvider _connectionStringProvider;
    private readonly IFileHashCalculator _hashCalculator;
    private readonly IFilePathResolver _pathResolver;
    private readonly IOperationalPauseCoordinator _pauseCoordinator;
    private readonly IStorageSpaceAnalyzer _spaceAnalyzer;
    private readonly ILogger<ImportPackageService> _logger;

    public ImportPackageService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IConnectionStringProvider connectionStringProvider,
        IFileHashCalculator hashCalculator,
        IFilePathResolver pathResolver,
        IOperationalPauseCoordinator pauseCoordinator,
        IStorageSpaceAnalyzer spaceAnalyzer,
        ILogger<ImportPackageService> logger)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
        _hashCalculator = hashCalculator ?? throw new ArgumentNullException(nameof(hashCalculator));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _pauseCoordinator = pauseCoordinator ?? throw new ArgumentNullException(nameof(pauseCoordinator));
        _spaceAnalyzer = spaceAnalyzer ?? throw new ArgumentNullException(nameof(spaceAnalyzer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<StorageOperationResult> ImportPackageAsync(
        string packageRoot,
        string targetStorageRoot,
        StorageImportOptions? options,
        CancellationToken cancellationToken)
    {
        options ??= new StorageImportOptions();

        var normalizedPackageRoot = Path.GetFullPath(packageRoot);
        if (!Directory.Exists(normalizedPackageRoot))
        {
            return new StorageOperationResult
            {
                Status = StorageOperationStatus.InvalidPackage,
                Message = $"Package directory '{normalizedPackageRoot}' was not found.",
            };
        }

        var metadataPath = Path.Combine(normalizedPackageRoot, MetadataFileName);
        if (!File.Exists(metadataPath))
        {
            return new StorageOperationResult
            {
                Status = StorageOperationStatus.InvalidPackage,
                Message = "Package is missing metadata.json.",
            };
        }

        var metadata = await ReadMetadataAsync(metadataPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(metadata.FormatVersion, StoragePackageMetadata.CurrentFormatVersion, StringComparison.Ordinal))
        {
            return new StorageOperationResult
            {
                Status = StorageOperationStatus.InvalidPackage,
                Message = $"Unsupported package format version '{metadata.FormatVersion}'.",
            };
        }

        var normalizedTargetRoot = SafePathUtilities.NormalizeAndValidateRoot(targetStorageRoot, _logger);
        var dbDirectory = Path.Combine(normalizedPackageRoot, DatabaseDirectory);
        var sourceDbPath = Path.Combine(dbDirectory, metadata.DatabaseFileName);
        if (!File.Exists(sourceDbPath))
        {
            return new StorageOperationResult
            {
                Status = StorageOperationStatus.InvalidPackage,
                Message = "Package database file is missing.",
            };
        }

        var storageSourceRoot = Path.Combine(normalizedPackageRoot, StorageDirectory);
        if (!Directory.Exists(storageSourceRoot))
        {
            return new StorageOperationResult
            {
                Status = StorageOperationStatus.InvalidPackage,
                Message = "Package storage directory is missing.",
            };
        }

        var packageFileHashes = metadata.FileHashes ?? new Dictionary<string, string>();

        var storageSize = await _spaceAnalyzer.CalculateDirectorySizeAsync(storageSourceRoot, cancellationToken).ConfigureAwait(false);
        var dbSize = await _spaceAnalyzer.GetFileSizeAsync(sourceDbPath, cancellationToken).ConfigureAwait(false);
        var required = (long)Math.Ceiling((storageSize + dbSize) * SafetyMargin);
        var available = Math.Min(
            await _spaceAnalyzer.GetAvailableBytesAsync(_connectionStringProvider.DatabasePath, cancellationToken).ConfigureAwait(false),
            await _spaceAnalyzer.GetAvailableBytesAsync(normalizedTargetRoot, cancellationToken).ConfigureAwait(false));

        if (available < required)
        {
            return new StorageOperationResult
            {
                Status = StorageOperationStatus.InsufficientSpace,
                Message = $"Insufficient space for import. Required {required} bytes, available {available} bytes.",
                RequiredBytes = required,
                AvailableBytes = available,
            };
        }

        await _pauseCoordinator.PauseAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var destinationDbPath = _connectionStringProvider.DatabasePath;
            SafePathUtilities.EnsureDirectoryForFile(destinationDbPath);

            await AtomicFileOperations.CopyAsync(sourceDbPath, destinationDbPath, options.OverwriteExisting, cancellationToken)
                .ConfigureAwait(false);

            var errors = new List<string>();
            var failedFiles = new List<string>();
            var missingFiles = new List<string>();
            var importedFiles = 0;
            var verificationFailures = 0;
            var verifiedFiles = 0;
            var databaseHashMatched = true;

            if (options.Verification.VerifyDatabaseHash && !string.IsNullOrWhiteSpace(metadata.DatabaseSha256))
            {
                var dbHash = await _hashCalculator.ComputeSha256Async(destinationDbPath, cancellationToken).ConfigureAwait(false);
                databaseHashMatched = string.Equals(dbHash.Value, metadata.DatabaseSha256, StringComparison.OrdinalIgnoreCase);
                if (!databaseHashMatched)
                {
                    errors.Add($"Database hash mismatch. Expected {metadata.DatabaseSha256} but found {dbHash.Value}.");
                    _logger.LogWarning(
                        "Database hash mismatch detected after import. Expected={ExpectedHash}, Actual={ActualHash}.",
                        metadata.DatabaseSha256,
                        dbHash.Value);
                }
            }

            foreach (var sourceFile in Directory.EnumerateFiles(storageSourceRoot, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(storageSourceRoot, sourceFile);
                if (SafePathUtilities.IsOutsideRoot(relativePath))
                {
                    errors.Add($"Package file {relativePath} is invalid or escapes the storage root.");
                    continue;
                }

                var destinationPath = Path.Combine(normalizedTargetRoot, SafePathUtilities.NormalizeRelative(relativePath, _logger));
                SafePathUtilities.EnsureDirectoryForFile(destinationPath);

                try
                {
                    await AtomicFileOperations.CopyAsync(sourceFile, destinationPath, options.OverwriteExisting, cancellationToken)
                        .ConfigureAwait(false);
                    importedFiles++;

                    if (options.Verification.VerifyFilesBySize)
                    {
                        var sourceInfo = new FileInfo(sourceFile);
                        var destinationInfo = new FileInfo(destinationPath);
                        if (sourceInfo.Length != destinationInfo.Length)
                        {
                            verificationFailures++;
                            failedFiles.Add(relativePath);
                            errors.Add($"Size mismatch for imported file {relativePath}: expected {sourceInfo.Length} bytes, found {destinationInfo.Length} bytes.");
                            continue;
                        }
                    }

                    if (options.Verification.VerifyFilesByHash && packageFileHashes.TryGetValue(relativePath, out var expectedHash))
                    {
                        var computed = await _hashCalculator.ComputeSha256Async(destinationPath, cancellationToken).ConfigureAwait(false);
                        if (!string.Equals(expectedHash, computed.Value, StringComparison.OrdinalIgnoreCase))
                        {
                            verificationFailures++;
                            failedFiles.Add(relativePath);
                            errors.Add($"Hash mismatch for imported file {relativePath}.");
                            continue;
                        }
                    }

                    verifiedFiles++;
                }
                catch (FileNotFoundException)
                {
                    missingFiles.Add(relativePath);
                    _logger.LogWarning("Missing imported file {RelativePath}.", relativePath);
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to import {relativePath}: {ex.Message}");
                    _logger.LogError(ex, "Failed to import {RelativePath} from package.", relativePath);
                }
            }

            await using (var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var pendingMigrations = await dbContext.Database
                    .GetPendingMigrationsAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (pendingMigrations.Any())
                {
                    return new StorageOperationResult
                    {
                        Status = StorageOperationStatus.PendingMigrations,
                        Message = "Imported database requires migrations. Apply migrations before continuing.",
                        PackageRoot = normalizedPackageRoot,
                        TargetStorageRoot = normalizedTargetRoot,
                        MissingFiles = missingFiles,
                        FailedFiles = failedFiles,
                        Errors = errors,
                        FailedVerificationCount = verificationFailures,
                        VerifiedFilesCount = verifiedFiles,
                        DatabaseHashMatched = databaseHashMatched,
                        AffectedFiles = importedFiles,
                    };
                }

                var appliedMigrations = await dbContext.Database
                    .GetAppliedMigrationsAsync(cancellationToken)
                    .ConfigureAwait(false);

                var currentSchema = appliedMigrations.LastOrDefault();
                if (!string.IsNullOrWhiteSpace(metadata.SchemaVersion)
                    && !string.Equals(metadata.SchemaVersion, currentSchema, StringComparison.Ordinal))
                {
                    errors.Add($"Schema version mismatch between package ({metadata.SchemaVersion}) and imported database ({currentSchema ?? "<none>"}).");
                }

                var storageRoot = await dbContext.StorageRoots.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                if (storageRoot is null)
                {
                    dbContext.StorageRoots.Add(new FileStorageRootEntity(normalizedTargetRoot));
                }
                else
                {
                    storageRoot.UpdateRootPath(normalizedTargetRoot);
                }

                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                if (_pathResolver is FilePathResolver resolver)
                {
                    resolver.OverrideCachedRoot(normalizedTargetRoot);
                }
            }

            _logger.LogInformation(
                "Import completed from {PackageRoot} to {TargetRoot}. Files imported: {ImportedFiles} (verification failures: {VerificationFailures}).",
                normalizedPackageRoot,
                normalizedTargetRoot,
                importedFiles,
                verificationFailures);

            var status = StorageOperationStatus.Success;
            if (errors.Any() || missingFiles.Any() || verificationFailures > 0 || !databaseHashMatched)
            {
                status = errors.Any() ? StorageOperationStatus.PartialSuccess : StorageOperationStatus.PartialSuccess;
            }

            return new StorageOperationResult
            {
                Status = status,
                PackageRoot = normalizedPackageRoot,
                TargetStorageRoot = normalizedTargetRoot,
                AffectedFiles = importedFiles,
                MissingFiles = missingFiles,
                FailedFiles = failedFiles,
                Errors = errors,
                FailedVerificationCount = verificationFailures,
                VerifiedFilesCount = verifiedFiles,
                DatabaseHashMatched = databaseHashMatched,
                Message = status == StorageOperationStatus.Success ? "Import completed." : "Import completed with warnings.",
            };
        }
        finally
        {
            _pauseCoordinator.Resume();
        }
    }

    private static async Task<StoragePackageMetadata> ReadMetadataAsync(string metadataPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(metadataPath);
        var metadata = await JsonSerializer.DeserializeAsync<StoragePackageMetadata>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (metadata is null)
        {
            throw new InvalidOperationException("Package metadata could not be read.");
        }

        return metadata;
    }
}
