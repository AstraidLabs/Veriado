// File: Veriado.Services/Storage/IStorageManagementService.cs
using Veriado.Application.Abstractions;
using Veriado.Contracts.Storage;

namespace Veriado.Services.Storage;

/// <summary>
/// Provides a façade over storage root management, migration, export, and import operations.
/// </summary>
public interface IStorageManagementService
{
    Task<string> GetCurrentRootAsync(CancellationToken cancellationToken);

    Task<string> GetEffectiveRootAsync(CancellationToken cancellationToken);

    Task ChangeRootAsync(string newRoot, CancellationToken cancellationToken);

    Task<StorageOperationResultDto> MigrateRootAsync(
        string newRoot,
        StorageMigrationOptionsDto? options,
        CancellationToken cancellationToken);

    Task<StorageOperationResultDto> ExportAsync(
        string packageRoot,
        StorageExportOptionsDto? options,
        CancellationToken cancellationToken);

    Task<StorageOperationResultDto> ExportAsync(
        ExportRequestDto request,
        CancellationToken cancellationToken);

    Task<StorageOperationResultDto> ImportAsync(
        string packageRoot,
        string targetStorageRoot,
        StorageImportOptionsDto? options,
        CancellationToken cancellationToken);

    Task<ImportValidationResultDto> ValidateImportAsync(
        ImportRequestDto request,
        CancellationToken cancellationToken);

    Task<ImportCommitResultDto> CommitImportAsync(
        ImportRequestDto request,
        ImportConflictStrategy conflictStrategy,
        CancellationToken cancellationToken);
}
