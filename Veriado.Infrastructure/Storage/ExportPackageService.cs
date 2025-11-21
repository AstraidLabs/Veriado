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

public sealed class ExportPackageService : IExportPackageService
{
    private const string MetadataFileName = "metadata.json";
    private const string DatabaseDirectory = "db";
    private const string StorageDirectory = "storage";

    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IConnectionStringProvider _connectionStringProvider;
    private readonly IFileHashCalculator _hashCalculator;
    private readonly ILogger<ExportPackageService> _logger;

    public ExportPackageService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IConnectionStringProvider connectionStringProvider,
        IFileHashCalculator hashCalculator,
        ILogger<ExportPackageService> logger)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
        _hashCalculator = hashCalculator ?? throw new ArgumentNullException(nameof(hashCalculator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        var normalizedRoot = SafePathUtilities.NormalizeAndValidateRoot(storageRoot.RootPath, _logger);
        var files = await dbContext.FileSystems
            .AsNoTracking()
            .Select(f => new { f.RelativePath, f.Size })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var databaseTargetDirectory = Path.Combine(normalizedPackageRoot, DatabaseDirectory);
        Directory.CreateDirectory(databaseTargetDirectory);
        var targetDatabasePath = Path.Combine(databaseTargetDirectory, Path.GetFileName(_connectionStringProvider.DatabasePath));
        await AtomicFileOperations.CopyAsync(_connectionStringProvider.DatabasePath, targetDatabasePath, overwrite: true, cancellationToken)
            .ConfigureAwait(false);

        var storageTargetRoot = Path.Combine(normalizedPackageRoot, StorageDirectory);
        Directory.CreateDirectory(storageTargetRoot);

        var missingFiles = 0;
        var exportedFiles = 0;
        long totalBytes = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = SafePathUtilities.NormalizeRelative(file.RelativePath.Value, _logger);
            var sourcePath = Path.Combine(normalizedRoot, relativePath);
            var destinationPath = Path.Combine(storageTargetRoot, relativePath);

            try
            {
                await AtomicFileOperations.CopyAsync(sourcePath, destinationPath, overwrite: true, cancellationToken)
                    .ConfigureAwait(false);
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
            catch (FileNotFoundException)
            {
                missingFiles++;
                _logger.LogWarning("Source file {SourcePath} missing during export.", sourcePath);
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
}
