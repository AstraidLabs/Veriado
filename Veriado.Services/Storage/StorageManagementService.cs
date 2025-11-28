// File: Veriado.Services/Storage/StorageManagementService.cs
using System.Linq;
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

    public async Task<StorageOperationResultDto> MigrateRootAsync(
        string newRoot,
        StorageMigrationOptionsDto? options,
        CancellationToken cancellationToken)
    {
        var result = await _migrationService
            .MigrateStorageRootAsync(newRoot, Map(options), cancellationToken)
            .ConfigureAwait(false);

        return Map(result);
    }

    public async Task<StorageOperationResultDto> ExportAsync(
        string packageRoot,
        StorageExportOptionsDto? options,
        CancellationToken cancellationToken)
    {
        var result = await _exportService
            .ExportPackageAsync(packageRoot, Map(options), cancellationToken)
            .ConfigureAwait(false);

        return Map(result);
    }

    public async Task<StorageOperationResultDto> ImportAsync(
        string packageRoot,
        string targetStorageRoot,
        StorageImportOptionsDto? options,
        CancellationToken cancellationToken)
    {
        var result = await _importService
            .ImportPackageAsync(packageRoot, targetStorageRoot, Map(options), cancellationToken)
            .ConfigureAwait(false);

        return Map(result);
    }

    public async Task<ImportValidationResultDto> ValidateImportAsync(
        ImportRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = await _importService
            .ValidateLogicalPackageAsync(Map(request), cancellationToken)
            .ConfigureAwait(false);

        return Map(validation);
    }

    public async Task<ImportCommitResultDto> CommitImportAsync(
        ImportRequestDto request,
        ImportConflictStrategy conflictStrategy,
        CancellationToken cancellationToken)
    {
        var commit = await _importService
            .CommitLogicalPackageAsync(Map(request), conflictStrategy, cancellationToken)
            .ConfigureAwait(false);

        return Map(commit);
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
            Verification = Map(dto.Verification),
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
            IncludeFileHashes = dto.IncludeFileHashes,
            ExportMode = dto.ExportMode,
            Verification = Map(dto.Verification),
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
            Verification = Map(dto.Verification),
        };
    }

    private static ImportRequest Map(ImportRequestDto dto)
        => new()
        {
            PackagePath = dto.PackagePath,
            ScopeFilter = dto.ScopeFilter,
            TargetStorageRoot = dto.TargetStorageRoot,
        };

    private static StorageOperationResultDto Map(StorageOperationResult result)
    {
        return new StorageOperationResultDto
        {
            Status = Map(result.Status),
            Message = result.Message,
            MissingFiles = result.MissingFiles,
            FailedFiles = result.FailedFiles,
            DatabaseHashMatched = result.DatabaseHashMatched,
            VerifiedFilesCount = result.VerifiedFilesCount,
            FailedVerificationCount = result.FailedVerificationCount,
            RequiredBytes = result.RequiredBytes,
            AvailableBytes = result.AvailableBytes,
            PackageRoot = result.PackageRoot,
            TargetStorageRoot = result.TargetStorageRoot,
            DatabasePath = result.DatabasePath,
            AffectedFiles = result.AffectedFiles,
            Errors = result.Errors,
        };
    }

    private static ImportValidationResultDto Map(ImportValidationResult result)
    {
        return new ImportValidationResultDto
        {
            IsValid = result.IsValid,
            DiscoveredDescriptors = result.DiscoveredDescriptors,
            DiscoveredFiles = result.DiscoveredFiles,
            TotalBytes = result.TotalBytes,
            Issues = result.Issues
                .Select(Map)
                .ToArray(),
            ValidatedFiles = result.ValidatedFiles
                .Select(Map)
                .ToArray(),
            Items = result.Items
                .Select(Map)
                .ToArray(),
            NewItems = result.NewItems,
            UpdatableItems = result.UpdatableItems,
            SkippedItems = result.SkippedItems,
        };
    }

    private static ImportCommitResultDto Map(ImportCommitResult result)
    {
        return new ImportCommitResultDto
        {
            Status = result.Status,
            ImportedFiles = result.ImportedFiles,
            SkippedFiles = result.SkippedFiles,
            ConflictedFiles = result.ConflictedFiles,
            Issues = result.Issues.Select(Map).ToArray(),
            Items = result.Items.Select(Map).ToArray(),
        };
    }

    private static ImportValidationIssueDto Map(ImportValidationIssue issue)
        => new()
        {
            Type = issue.Type,
            Severity = issue.Severity,
            RelativePath = issue.RelativePath,
            Message = issue.Message,
        };

    private static ValidatedImportFileDto Map(ValidatedImportFile file)
        => new()
        {
            RelativePath = file.RelativePath,
            FileName = file.FileName,
            DescriptorPath = file.DescriptorPath,
            FileId = file.FileId,
            ContentHash = file.ContentHash,
            SizeBytes = file.SizeBytes,
            MimeType = file.MimeType,
            LastModifiedAtUtc = file.LastModifiedAtUtc,
        };

    private static ImportItemPreviewDto Map(ImportItemPreview preview)
        => new()
        {
            FileId = preview.FileId,
            RelativePath = preview.RelativePath,
            FileName = preview.FileName,
            ContentHash = preview.ContentHash,
            SizeBytes = preview.SizeBytes,
            LastModifiedAtUtc = preview.LastModifiedAtUtc,
            Status = preview.Status,
            ConflictReason = preview.ConflictReason,
        };

    private static StorageVerificationOptions Map(StorageVerificationOptionsDto? dto)
    {
        if (dto is null)
        {
            return new StorageVerificationOptions();
        }

        return new StorageVerificationOptions
        {
            VerifyDatabaseHash = dto.VerifyDatabaseHash,
            VerifyFilesByHash = dto.VerifyFilesByHash,
            VerifyFilesBySize = dto.VerifyFilesBySize,
        };
    }

    private static StorageOperationStatusDto Map(StorageOperationStatus status)
        => status switch
        {
            StorageOperationStatus.Success => StorageOperationStatusDto.Success,
            StorageOperationStatus.PartialSuccess => StorageOperationStatusDto.PartialSuccess,
            StorageOperationStatus.Failed => StorageOperationStatusDto.Failed,
            StorageOperationStatus.InsufficientSpace => StorageOperationStatusDto.InsufficientSpace,
            StorageOperationStatus.InvalidPackage => StorageOperationStatusDto.InvalidPackage,
            StorageOperationStatus.SchemaMismatch => StorageOperationStatusDto.SchemaMismatch,
            StorageOperationStatus.PendingMigrations => StorageOperationStatusDto.PendingMigrations,
            _ => StorageOperationStatusDto.Failed,
        };
}
