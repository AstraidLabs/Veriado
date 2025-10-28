namespace Veriado.Services.Files;

/// <summary>
/// Provides orchestration helpers for file mutations.
/// </summary>
public interface IFileOperationsService
{
    Task<ApiResponse<Guid>> RenameAsync(Guid fileId, string newName, CancellationToken cancellationToken);

    Task<ApiResponse<Guid>> UpdateMetadataAsync(
        Guid fileId,
        FileMetadataPatchDto patch,
        int? expectedVersion,
        CancellationToken cancellationToken);

    Task<ApiResponse<Guid>> SetReadOnlyAsync(Guid fileId, bool isReadOnly, CancellationToken cancellationToken);

    Task<ApiResponse<Guid>> SetValidityAsync(
        Guid fileId,
        FileValidityDto validity,
        int? expectedVersion,
        CancellationToken cancellationToken);

    Task<ApiResponse<Guid>> ClearValidityAsync(Guid fileId, int? expectedVersion, CancellationToken cancellationToken);

    Task<ApiResponse<Guid>> ReplaceContentAsync(Guid fileId, byte[] content, CancellationToken cancellationToken);

    Task<ApiResponse<Guid>> ApplySystemMetadataAsync(Guid fileId, FileSystemMetadataDto metadata, CancellationToken cancellationToken);
}
