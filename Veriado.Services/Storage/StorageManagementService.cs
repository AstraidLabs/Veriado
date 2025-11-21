// File: Veriado.Services/Storage/StorageManagementService.cs
using Veriado.Application.Abstractions;
using Veriado.Contracts.Storage;

namespace Veriado.Services.Storage;

/// <summary>
/// Implements orchestration over storage root, migration, export, and import operations.
/// </summary>
public sealed class StorageManagementService : IStorageManagementService
{
    private readonly IStorageRootSettingsService _rootSettings;
    private readonly IStorageMigrationService _migrationService;
    private readonly IExportPackageService _exportService;
    private readonly IImportPackageService _importService;

    public StorageManagementService(
        IStorageRootSettingsService rootSettings,
        IStorageMigrationService migrationService,
        IExportPackageService exportService,
        IImportPackageService importService)
    {
        _rootSettings = rootSettings ?? throw new ArgumentNullException(nameof(rootSettings));
        _migrationService = migrationService ?? throw new ArgumentNullException(nameof(migrationService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
    }

    public Task<string> GetCurrentRootAsync(CancellationToken cancellationToken)
        => _rootSettings.GetCurrentRootAsync(cancellationToken);

    public Task<string> GetEffectiveRootAsync(CancellationToken cancellationToken)
        => _rootSettings.GetEffectiveRootAsync(cancellationToken);

    public Task ChangeRootAsync(string newRoot, CancellationToken cancellationToken)
        => _rootSettings.ChangeRootAsync(newRoot, cancellationToken);

    public async Task<StorageMigrationResultDto> MigrateRootAsync(
        string newRoot,
        StorageMigrationOptionsDto? options,
        CancellationToken cancellationToken)
    {
        var result = await _migrationService
            .MigrateStorageRootAsync(newRoot, Map(options), cancellationToken)
            .ConfigureAwait(false);

        return new StorageMigrationResultDto(
            result.OldRoot,
            result.NewRoot,
            result.MigratedFiles,
            result.MissingSources,
            result.VerificationFailures,
            result.Errors);
    }

    public async Task<StorageExportResultDto> ExportAsync(
        string packageRoot,
        StorageExportOptionsDto? options,
        CancellationToken cancellationToken)
    {
        var result = await _exportService
            .ExportPackageAsync(packageRoot, Map(options), cancellationToken)
            .ConfigureAwait(false);

        return new StorageExportResultDto(
            result.PackageRoot,
            result.DatabasePath,
            result.ExportedFiles,
            result.MissingFiles);
    }

    public async Task<StorageImportResultDto> ImportAsync(
        string packageRoot,
        string targetStorageRoot,
        StorageImportOptionsDto? options,
        CancellationToken cancellationToken)
    {
        var result = await _importService
            .ImportPackageAsync(packageRoot, targetStorageRoot, Map(options), cancellationToken)
            .ConfigureAwait(false);

        return new StorageImportResultDto(
            result.PackageRoot,
            result.TargetStorageRoot,
            result.ImportedFiles,
            result.VerificationFailures,
            result.Errors);
    }

    private static StorageMigrationOptions? Map(StorageMigrationOptionsDto? dto)
    {
        if (dto is null)
        {
            return null;
        }

        return new StorageMigrationOptions
        {
            DeleteSourceAfterCopy = dto.DeleteSourceAfterCopy,
            VerifyHashes = dto.VerifyHashes,
        };
    }

    private static StorageExportOptions? Map(StorageExportOptionsDto? dto)
    {
        if (dto is null)
        {
            return null;
        }

        return new StorageExportOptions
        {
            OverwriteExisting = dto.OverwriteExisting,
        };
    }

    private static StorageImportOptions? Map(StorageImportOptionsDto? dto)
    {
        if (dto is null)
        {
            return null;
        }

        return new StorageImportOptions
        {
            OverwriteExisting = dto.OverwriteExisting,
            VerifyAfterCopy = dto.VerifyAfterCopy,
        };
    }
}
