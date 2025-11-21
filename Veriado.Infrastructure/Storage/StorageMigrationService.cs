using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Veriado.Application.Abstractions;
using Veriado.Infrastructure.FileSystem;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Connections;

namespace Veriado.Infrastructure.Storage;

/// <summary>
/// Provides storage migration, export, and import capabilities.
/// </summary>
public sealed class StorageMigrationService : IStorageMigrationService
{
    private const string MetadataFileName = "metadata.json";
    private const string DatabaseDirectory = "db";
    private const string StorageDirectory = "storage";

    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IConnectionStringProvider _connectionStringProvider;
    private readonly IFileHashCalculator _hashCalculator;
    private readonly IFilePathResolver _pathResolver;
    private readonly ILogger<StorageMigrationService> _logger;

    public StorageMigrationService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IConnectionStringProvider connectionStringProvider,
        IFileHashCalculator hashCalculator,
        IFilePathResolver pathResolver,
        ILogger<StorageMigrationService> logger)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
        _hashCalculator = hashCalculator ?? throw new ArgumentNullException(nameof(hashCalculator));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<StorageMigrationResult> MigrateStorageRootAsync(
        string newRootPath,
        StorageMigrationOptions? options,
        CancellationToken cancellationToken)
    {
        options ??= new StorageMigrationOptions();

        var normalizedTargetRoot = NormalizeRootPath(StorageRootValidator.ValidateWritableRoot(newRootPath, _logger));
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var storageRoot = await dbContext.StorageRoots
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Storage root is not configured.");

        var normalizedOldRoot = NormalizeRootPath(StorageRootValidator.ValidateWritableRoot(storageRoot.RootPath, _logger));

        if (string.Equals(normalizedOldRoot, normalizedTargetRoot, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Requested storage root matches the current value; skipping migration.");
            return new StorageMigrationResult(normalizedOldRoot, normalizedTargetRoot);
        }

        _logger.LogInformation(
            "Beginning storage migration from {OldRoot} to {NewRoot}.",
            normalizedOldRoot,
            normalizedTargetRoot);

        var files = await dbContext.FileSystems
            .AsNoTracking()
            .Select(f => new { f.Id, f.RelativePath, f.Size, f.Hash })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var errors = new List<string>();
        var migrated = 0;
        var missing = 0;
        var verificationFailures = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = NormalizeRelativeForPlatform(file.RelativePath.Value);
            var sourcePath = Path.Combine(normalizedOldRoot, relativePath);
            var destinationPath = Path.Combine(normalizedTargetRoot, relativePath);

            try
            {
                EnsureDirectory(destinationPath);
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to create directory for {relativePath}: {ex.Message}");
                _logger.LogError(ex, "Failed to prepare directory for {RelativePath}.", relativePath);
                continue;
            }

            if (!File.Exists(sourcePath))
            {
                missing++;
                _logger.LogWarning("Source file {SourcePath} missing during migration.", sourcePath);
                continue;
            }

            var tempDestination = destinationPath + $".tmp-{Guid.NewGuid():N}";

            try
            {
                File.Copy(sourcePath, tempDestination, overwrite: true);
                File.Move(tempDestination, destinationPath, overwrite: true);

                if (options.DeleteSourceAfterCopy)
                {
                    TryDelete(sourcePath);
                }

                migrated++;
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to migrate {relativePath}: {ex.Message}");
                _logger.LogError(ex, "Failed to migrate {RelativePath}.", relativePath);
                TryDelete(tempDestination);
            }
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = NormalizeRelativeForPlatform(file.RelativePath.Value);
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

    public async Task<StorageExportResult> ExportPackageAsync(
        string packageRoot,
        StorageExportOptions? options,
        CancellationToken cancellationToken)
    {
        options ??= new StorageExportOptions();

        var normalizedPackageRoot = Path.GetFullPath(packageRoot);
        PreparePackageDirectory(normalizedPackageRoot, options.OverwriteExisting);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var pendingMigrations = await dbContext.Database
            .GetPendingMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);

        if (pendingMigrations.Any())
        {
            throw new InvalidOperationException("Cannot export while database migrations are pending. Please update the database first.");
        }

        var storageRoot = await dbContext.StorageRoots
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Storage root is not configured.");

        var normalizedRoot = StorageRootValidator.ValidateWritableRoot(storageRoot.RootPath, _logger);
        var files = await dbContext.FileSystems
            .AsNoTracking()
            .Select(f => new { f.Id, f.RelativePath, f.Size })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var databaseTargetDirectory = Path.Combine(normalizedPackageRoot, DatabaseDirectory);
        Directory.CreateDirectory(databaseTargetDirectory);
        var targetDatabasePath = Path.Combine(databaseTargetDirectory, Path.GetFileName(_connectionStringProvider.DatabasePath));
        await CopyWithAtomicMoveAsync(_connectionStringProvider.DatabasePath, targetDatabasePath, overwrite: true, cancellationToken).ConfigureAwait(false);

        var storageTargetRoot = Path.Combine(normalizedPackageRoot, StorageDirectory);
        Directory.CreateDirectory(storageTargetRoot);

        var missingFiles = 0;
        var exportedFiles = 0;
        long totalBytes = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = NormalizeRelativeForPlatform(file.RelativePath.Value);
            var sourcePath = Path.Combine(normalizedRoot, relativePath);
            var destinationPath = Path.Combine(storageTargetRoot, relativePath);

            try
            {
                EnsureDirectory(destinationPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to prepare export directory for {RelativePath}.", relativePath);
                throw;
            }

            if (!File.Exists(sourcePath))
            {
                missingFiles++;
                _logger.LogWarning("Source file {SourcePath} missing during export.", sourcePath);
                continue;
            }

            await CopyWithAtomicMoveAsync(sourcePath, destinationPath, overwrite: true, cancellationToken).ConfigureAwait(false);
            exportedFiles++;

            try
            {
                totalBytes += new FileInfo(sourcePath).Length;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read size information for {SourcePath} while exporting.", sourcePath);
            }
        }

        var metadata = new StoragePackageMetadata
        {
            FormatVersion = StoragePackageMetadata.CurrentFormatVersion,
            ApplicationVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
            SchemaVersion = (await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).LastOrDefault(),
            OriginalStorageRoot = normalizedRoot,
            FileCount = files.Count,
            MissingFiles = missingFiles,
            TotalSize = totalBytes,
            DatabaseFileName = Path.GetFileName(targetDatabasePath),
            DatabaseSha256 = (await _hashCalculator.ComputeSha256Async(targetDatabasePath, cancellationToken).ConfigureAwait(false)).Value,
            ExportedAtUtc = DateTimeOffset.UtcNow,
        };

        await WriteMetadataAsync(Path.Combine(normalizedPackageRoot, MetadataFileName), metadata, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Export completed to {PackageRoot}. Files exported: {ExportedFiles}, missing: {MissingFiles}.",
            normalizedPackageRoot,
            exportedFiles,
            missingFiles);

        return new StorageExportResult(normalizedPackageRoot)
        {
            DatabasePath = targetDatabasePath,
            ExportedFiles = exportedFiles,
            MissingFiles = missingFiles,
        };
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

        var normalizedTargetRoot = NormalizeRootPath(StorageRootValidator.ValidateWritableRoot(targetStorageRoot, _logger));

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

        var destinationDbPath = _connectionStringProvider.DatabasePath;
        EnsureDirectory(destinationDbPath);

        await CopyWithAtomicMoveAsync(sourceDbPath, destinationDbPath, options.OverwriteExisting, cancellationToken).ConfigureAwait(false);

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
            if (IsOutsideRoot(relativePath))
            {
                errors.Add($"Package file {relativePath} is invalid or escapes the storage root.");
                continue;
            }

            var destinationPath = Path.Combine(normalizedTargetRoot, NormalizeRelativeForPlatform(relativePath));
            EnsureDirectory(destinationPath);

            try
            {
                await CopyWithAtomicMoveAsync(sourceFile, destinationPath, options.OverwriteExisting, cancellationToken).ConfigureAwait(false);
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
                var destinationPath = Path.Combine(normalizedTargetRoot, NormalizeRelativeForPlatform(relativePath));

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

                    if (!string.IsNullOrWhiteSpace(metadata.DatabaseSha256))
                    {
                        // Verification of storage files relies on size unless hash validation is specifically requested.
                        // Additional hash checks could be added in future iterations.
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
                dbContext.StorageRoots.Add(new Persistence.Entities.FileStorageRootEntity(normalizedTargetRoot));
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

    private static void EnsureDirectory(string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string NormalizeRelativeForPlatform(string relativePath)
    {
        return relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private static string NormalizeRootPath(string rootPath)
    {
        return Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsOutsideRoot(string relativePath)
    {
        return string.IsNullOrWhiteSpace(relativePath)
            || relativePath.StartsWith("..", StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private Task CopyWithAtomicMoveAsync(
        string source,
        string destination,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tempDestination = destination + $".tmp-{Guid.NewGuid():N}";
        EnsureDirectory(destination);

        try
        {
            File.Copy(source, tempDestination, overwrite: true);
            File.Move(tempDestination, destination, overwrite: overwrite);
        }
        catch
        {
            TryDelete(tempDestination);
            throw;
        }

        return Task.CompletedTask;
    }

    private static void PreparePackageDirectory(string packageRoot, bool overwriteExisting)
    {
        Directory.CreateDirectory(packageRoot);

        if (!overwriteExisting && Directory.EnumerateFileSystemEntries(packageRoot).Any())
        {
            throw new InvalidOperationException($"Package directory '{packageRoot}' is not empty.");
        }
    }

    private static async Task WriteMetadataAsync(string metadataPath, StoragePackageMetadata metadata, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(metadataPath);
        await JsonSerializer.SerializeAsync(stream, metadata, new JsonSerializerOptions { WriteIndented = true }, cancellationToken)
            .ConfigureAwait(false);
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

/// <summary>
/// Metadata captured for portable storage packages.
/// </summary>
public sealed record StoragePackageMetadata
{
    public const string CurrentFormatVersion = "1.0";

    public string FormatVersion { get; init; } = CurrentFormatVersion;
    public string ApplicationVersion { get; init; } = string.Empty;
    public string? SchemaVersion { get; init; }
    public string OriginalStorageRoot { get; init; } = string.Empty;
    public int FileCount { get; init; }
    public int MissingFiles { get; init; }
    public long TotalSize { get; init; }
    public string DatabaseFileName { get; init; } = string.Empty;
    public string? DatabaseSha256 { get; init; }
    public DateTimeOffset ExportedAtUtc { get; init; }
}
