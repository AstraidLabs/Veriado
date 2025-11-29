using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Veriado.Application.Abstractions;
using Veriado.Contracts.Storage;
using Contracts = Veriado.Contracts.Storage;
using AppVtpPackageInfo = Veriado.Application.Abstractions.VtpPackageInfo;
using AppVtpPayloadType = Veriado.Application.Abstractions.VtpPayloadType;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Connections;
using Veriado.Infrastructure.Storage.Vpf;
using Veriado.Infrastructure.Storage.Vpack;

namespace Veriado.Infrastructure.Storage;

public sealed class ExportPackageService : IExportPackageService
{
    private const double SafetyMargin = 1.1d;

    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IConnectionStringProvider _connectionStringProvider;
    private readonly IFileHashCalculator _hashCalculator;
    private readonly IStorageSpaceAnalyzer _spaceAnalyzer;
    private readonly ILogger<ExportPackageService> _logger;
    private readonly IVPackContainerService _vpackService;

    public ExportPackageService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IConnectionStringProvider connectionStringProvider,
        IFileHashCalculator hashCalculator,
        IStorageSpaceAnalyzer spaceAnalyzer,
        ILogger<ExportPackageService> logger,
        IVPackContainerService vpackService)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
        _hashCalculator = hashCalculator ?? throw new ArgumentNullException(nameof(hashCalculator));
        _spaceAnalyzer = spaceAnalyzer ?? throw new ArgumentNullException(nameof(spaceAnalyzer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _vpackService = vpackService ?? throw new ArgumentNullException(nameof(vpackService));
    }

    public async Task<StorageOperationResult> ExportPackageAsync(
        string packageRoot,
        StorageExportOptions? options,
        CancellationToken cancellationToken)
    {
        options ??= new StorageExportOptions();

        if (options.ExportMode != StorageExportMode.LogicalPerFile)
        {
            _logger.LogWarning("Physical exports are no longer supported; running logical per-file export instead.");
        }

        var request = new ExportRequest
        {
            DestinationPath = packageRoot,
            OverwriteExisting = options.OverwriteExisting,
        };

        return await ExportPackageAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StorageOperationResult> ExportPackageAsync(ExportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalizedDestination = Path.GetFullPath(request.DestinationPath);
        var destinationDirectory = Path.GetDirectoryName(normalizedDestination);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new InvalidOperationException("Destination directory cannot be determined for export.");
        }

        Directory.CreateDirectory(destinationDirectory);
        if (File.Exists(normalizedDestination) && !request.OverwriteExisting)
        {
            return new StorageOperationResult
            {
                Status = StorageOperationStatus.Failed,
                Message = $"Destination '{normalizedDestination}' already exists.",
                PackageRoot = normalizedDestination,
                WarningCount = 0,
                MissingFilesCount = 0,
            };
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), $"veriado-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var cleanup = true;
        try
        {
            var vpfRoot = Path.Combine(tempRoot, "vpf");
            Directory.CreateDirectory(vpfRoot);

            var result = await ExportLogicalPackageAsync(vpfRoot, request, cancellationToken).ConfigureAwait(false);
            if (result.Status is StorageOperationStatus.Failed or StorageOperationStatus.InsufficientSpace)
            {
                return result with { PackageRoot = normalizedDestination };
            }

            var zipPath = Path.Combine(tempRoot, "payload.zip");
            ZipFile.CreateFromDirectory(vpfRoot, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

            var tempVpack = Path.Combine(tempRoot, "payload.vpack");
            await using (var payloadStream = File.OpenRead(zipPath))
            await using (var output = File.Create(tempVpack))
            {
                var vpackOptions = new VPackCreateOptions
                {
                    EncryptPayload = request.EncryptPayload,
                    Password = request.Password,
                    SignPayload = request.SignPayload,
                    Vtp = result.Vtp,
                };

                await _vpackService.CreateContainerAsync(payloadStream, output, vpackOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempVpack, normalizedDestination, overwrite: request.OverwriteExisting);
            cleanup = false;

            return result with
            {
                PackageRoot = normalizedDestination,
                Message = "Export completed.",
            };
        }
        finally
        {
            if (cleanup && Directory.Exists(tempRoot))
            {
                try
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup temporary export folder {TempRoot}", tempRoot);
                }
            }
        }
    }

    private async Task<StorageOperationResult> ExportLogicalPackageAsync(
        string packageRoot,
        ExportRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedPackageRoot = Path.GetFullPath(packageRoot);
        PreparePackageDirectory(normalizedPackageRoot, overwriteExisting: true);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var pendingMigrations = await dbContext.Database
            .GetPendingMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);

        if (pendingMigrations.Any())
        {
            return new StorageOperationResult
            {
                Status = StorageOperationStatus.PendingMigrations,
                Message = "Cannot export while database migrations are pending. Please update the database first.",
                PackageRoot = normalizedPackageRoot,
                WarningCount = 0,
                MissingFilesCount = 0,
            };
        }

        var storageRoot = await dbContext.StorageRoots
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Storage root is not configured.");

        var normalizedRoot = SafePathUtilities.NormalizeAndValidateRoot(storageRoot.RootPath, _logger);
        var files = await dbContext.FileSystems
            .AsNoTracking()
            .Select(f => new
            {
                f.Id,
                f.RelativePath,
                f.Size,
                f.Hash,
                f.Mime,
                f.CreatedUtc,
                f.LastWriteUtc,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var totalBytes = files.Sum(f => f.Size.Value);
        var available = await _spaceAnalyzer.GetAvailableBytesAsync(normalizedPackageRoot, cancellationToken)
            .ConfigureAwait(false);
        var required = (long)Math.Ceiling(totalBytes * SafetyMargin);
        if (available < required)
        {
            return new StorageOperationResult
            {
                Status = StorageOperationStatus.InsufficientSpace,
                Message = $"Insufficient disk space for export. Required {required} bytes, available {available} bytes.",
                RequiredBytes = required,
                AvailableBytes = available,
                PackageRoot = normalizedPackageRoot,
                WarningCount = 0,
                MissingFilesCount = 0,
            };
        }

        var filesRoot = Path.Combine(normalizedPackageRoot, VpfPackagePaths.FilesDirectory);
        Directory.CreateDirectory(filesRoot);

        var missingFiles = new List<string>();
        var exportedFiles = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = SafePathUtilities.NormalizeRelative(file.RelativePath.Value, _logger);
            var sourcePath = Path.Combine(normalizedRoot, relativePath);
            var destinationPath = Path.Combine(filesRoot, relativePath);
            SafePathUtilities.EnsureDirectoryForFile(destinationPath);

            try
            {
                await AtomicFileOperations.CopyAsync(sourcePath, destinationPath, overwrite: true, cancellationToken)
                    .ConfigureAwait(false);
                exportedFiles++;

                var hash = await _hashCalculator.ComputeSha256Async(sourcePath, cancellationToken).ConfigureAwait(false);
                var descriptor = new ExportedFileDescriptor
                {
                    FileId = file.Id,
                    OriginalInstanceId = request.SourceInstanceId ?? Guid.Empty,
                    RelativePath = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? string.Empty,
                    FileName = Path.GetFileName(relativePath),
                    ContentHash = hash.Value,
                    SizeBytes = file.Size.Value,
                    MimeType = file.Mime.Value,
                    CreatedAtUtc = file.CreatedUtc.ToDateTimeOffset(),
                    CreatedBy = Environment.UserName,
                    LastModifiedAtUtc = file.LastWriteUtc.ToDateTimeOffset(),
                    LastModifiedBy = Environment.UserName,
                    IsReadOnly = false,
                };

                await WriteJsonAsync(destinationPath + ".json", descriptor, cancellationToken).ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                missingFiles.Add(relativePath);
                _logger.LogWarning("Source file {SourcePath} missing during logical export.", sourcePath);
            }
            catch (Exception ex)
            {
                missingFiles.Add(relativePath);
                _logger.LogError(ex, "Failed to export {RelativePath}.", relativePath);
            }
        }

        var packageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var vtpInfo = AppVtpPackageInfo
            .Default(packageId, correlationId, request.SourceInstanceId ?? Guid.Empty, targetInstanceId: Guid.Empty)
            with
            {
                PayloadType = AppVtpPayloadType.FullExport,
                SourceInstanceName = request.SourceInstanceName,
            };

        Contracts.VtpPackageInfo vtpContract = vtpInfo.ToContract();

        var manifest = new PackageJsonModel
        {
            PackageId = packageId,
            Name = request.PackageName ?? "Veriado Package",
            Description = request.Description,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedBy = Environment.UserName,
            SourceInstanceId = request.SourceInstanceId ?? Guid.Empty,
            SourceInstanceName = request.SourceInstanceName,
            ExportMode = "LogicalPerFile",
            Vtp = vtpContract,
        };

        var metadata = new MetadataJsonModel
        {
            FormatVersion = 1,
            ApplicationVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
            DatabaseSchemaVersion = (await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).LastOrDefault(),
            ExportMode = "LogicalPerFile",
            OriginalStorageRootPath = normalizedRoot,
            TotalFilesCount = files.Count,
            TotalFilesBytes = totalBytes,
            HashAlgorithm = "SHA256",
            FileDescriptorSchemaVersion = 1,
            Vtp = vtpContract,
        };

        await WriteJsonAsync(Path.Combine(normalizedPackageRoot, VpfPackagePaths.PackageManifestFile), manifest, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(Path.Combine(normalizedPackageRoot, VpfPackagePaths.MetadataFile), metadata, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Logical export completed to {PackageRoot}. Files exported: {ExportedFiles}, missing: {MissingFiles}.",
            normalizedPackageRoot,
            exportedFiles,
            missingFiles.Count);

        return new StorageOperationResult
        {
            Status = missingFiles.Count == 0 ? StorageOperationStatus.Success : StorageOperationStatus.PartialSuccess,
            PackageRoot = normalizedPackageRoot,
            AffectedFiles = exportedFiles,
            MissingFiles = missingFiles,
            MissingFilesCount = missingFiles.Count,
            FailedFilesCount = 0,
            WarningCount = missingFiles.Count,
            Warnings = missingFiles,
            Vtp = vtpInfo,
            Message = missingFiles.Count == 0 ? "Export completed." : "Export completed with missing files.",
        };
    }

    private static async Task WriteJsonAsync<T>(string path, T payload, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, payload, VpfSerialization.Options, cancellationToken).ConfigureAwait(false);
    }

    private static void PreparePackageDirectory(string packageRoot, bool overwriteExisting)
    {
        Directory.CreateDirectory(packageRoot);

        if (!overwriteExisting && Directory.EnumerateFileSystemEntries(packageRoot).Any())
        {
            throw new InvalidOperationException($"Package directory '{packageRoot}' is not empty.");
        }
    }

}
