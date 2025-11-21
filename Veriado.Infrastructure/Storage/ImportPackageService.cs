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

    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IConnectionStringProvider _connectionStringProvider;
    private readonly IFileHashCalculator _hashCalculator;
    private readonly IFilePathResolver _pathResolver;
    private readonly IOperationalPauseCoordinator _pauseCoordinator;
    private readonly ILogger<ImportPackageService> _logger;

    public ImportPackageService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IConnectionStringProvider connectionStringProvider,
        IFileHashCalculator hashCalculator,
        IFilePathResolver pathResolver,
        IOperationalPauseCoordinator pauseCoordinator,
        ILogger<ImportPackageService> logger)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
        _hashCalculator = hashCalculator ?? throw new ArgumentNullException(nameof(hashCalculator));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _pauseCoordinator = pauseCoordinator ?? throw new ArgumentNullException(nameof(pauseCoordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<StorageImportResult> ImportPackageAsync(
        string packageRoot,
        string targetStorageRoot,
        StorageImportOptions? options,
        CancellationToken cancellationToken)
    {
        options ??= new StorageImportOptions();

        var normalizedPackageRoot = Path.GetFullPath(packageRoot);
        if (!Directory.Exists(normalizedPackageRoot))
        {
            throw new DirectoryNotFoundException($"Package directory '{normalizedPackageRoot}' was not found.");
        }

        var metadataPath = Path.Combine(normalizedPackageRoot, MetadataFileName);
        if (!File.Exists(metadataPath))
        {
            throw new FileNotFoundException("Package is missing metadata.json.", metadataPath);
        }

        var metadata = await ReadMetadataAsync(metadataPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(metadata.FormatVersion, StoragePackageMetadata.CurrentFormatVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported package format version '{metadata.FormatVersion}'. Expected '{StoragePackageMetadata.CurrentFormatVersion}'.");
        }

        var normalizedTargetRoot = SafePathUtilities.NormalizeAndValidateRoot(targetStorageRoot, _logger);
        var dbDirectory = Path.Combine(normalizedPackageRoot, DatabaseDirectory);
        var sourceDbPath = Path.Combine(dbDirectory, metadata.DatabaseFileName);
        if (!File.Exists(sourceDbPath))
        {
            throw new FileNotFoundException("Package database file is missing.", sourceDbPath);
        }

        var storageSourceRoot = Path.Combine(normalizedPackageRoot, StorageDirectory);
        if (!Directory.Exists(storageSourceRoot))
        {
            throw new DirectoryNotFoundException("Package storage directory is missing.");
        }

        await _pauseCoordinator.PauseAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var destinationDbPath = _connectionStringProvider.DatabasePath;
            SafePathUtilities.EnsureDirectoryForFile(destinationDbPath);

            await AtomicFileOperations.CopyAsync(sourceDbPath, destinationDbPath, options.OverwriteExisting, cancellationToken)
                .ConfigureAwait(false);

            var errors = new List<string>();

            if (!string.IsNullOrWhiteSpace(metadata.DatabaseSha256))
            {
                var dbHash = await _hashCalculator.ComputeSha256Async(destinationDbPath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(dbHash.Value, metadata.DatabaseSha256, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Database hash mismatch. Expected {metadata.DatabaseSha256} but found {dbHash.Value}.");
                    _logger.LogWarning(
                        "Database hash mismatch detected after import. Expected={ExpectedHash}, Actual={ActualHash}.",
                        metadata.DatabaseSha256,
                        dbHash.Value);
                }
            }

            var importedFiles = 0;
            var verificationFailures = 0;

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
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to import {relativePath}: {ex.Message}");
                    _logger.LogError(ex, "Failed to import {RelativePath} from package.", relativePath);
                }
            }

            if (options.VerifyAfterCopy)
            {
                foreach (var sourceFile in Directory.EnumerateFiles(storageSourceRoot, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var relativePath = Path.GetRelativePath(storageSourceRoot, sourceFile);
                    var destinationPath = Path.Combine(normalizedTargetRoot, SafePathUtilities.NormalizeRelative(relativePath, _logger));

                    try
                    {
                        if (!File.Exists(destinationPath))
                        {
                            verificationFailures++;
                            errors.Add($"Missing imported file {relativePath}.");
                            continue;
                        }

                        var sourceInfo = new FileInfo(sourceFile);
                        var destinationInfo = new FileInfo(destinationPath);
                        if (sourceInfo.Length != destinationInfo.Length)
                        {
                            verificationFailures++;
                            errors.Add($"Size mismatch for imported file {relativePath}: expected {sourceInfo.Length} bytes, found {destinationInfo.Length} bytes.");
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        verificationFailures++;
                        errors.Add($"Verification failed for {relativePath}: {ex.Message}");
                        _logger.LogError(ex, "Verification failed for imported file {RelativePath}.", relativePath);
                    }
                }
            }

            await using (var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var pendingMigrations = await dbContext.Database
                    .GetPendingMigrationsAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (pendingMigrations.Any())
                {
                    throw new InvalidOperationException(
                        "Imported database requires migrations. Apply migrations before continuing to ensure schema compatibility.");
                }

                var appliedMigrations = await dbContext.Database
                    .GetAppliedMigrationsAsync(cancellationToken)
                    .ConfigureAwait(false);

                var currentSchema = appliedMigrations.LastOrDefault();
                if (!string.IsNullOrWhiteSpace(metadata.SchemaVersion)
                    && !string.Equals(metadata.SchemaVersion, currentSchema, StringComparison.Ordinal))
                {
                    var versionDetails = currentSchema ?? "<none>";
                    errors.Add($"Schema version mismatch between package ({metadata.SchemaVersion}) and imported database ({versionDetails}).");
                    _logger.LogWarning(
                        "Schema version mismatch detected during import. Package={PackageSchema}, Database={DatabaseSchema}.",
                        metadata.SchemaVersion,
                        versionDetails);
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

            return new StorageImportResult(normalizedPackageRoot, normalizedTargetRoot)
            {
                ImportedFiles = importedFiles,
                VerificationFailures = verificationFailures,
                Errors = errors,
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
