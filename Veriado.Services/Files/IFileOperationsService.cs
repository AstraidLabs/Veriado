namespace Veriado.Services.Files;

/// <summary>
/// Provides orchestration helpers for file mutations.
/// </summary>
public interface IFileOperationsService
{
    Task<ApiResponse<Guid>> RenameAsync(Guid fileId, string newName, CancellationToken cancellationToken);

    Task<ApiResponse<Guid>> UpdateMetadataAsync(UpdateMetadataRequest request, CancellationToken cancellationToken);

    Task<ApiResponse<Guid>> SetReadOnlyAsync(Guid fileId, bool isReadOnly, CancellationToken cancellationToken);

    Task<ApiResponse<Guid>> SetValidityAsync(Guid fileId, FileValidityDto validity, CancellationToken cancellationToken);

    Task<ApiResponse<Guid>> ClearValidityAsync(Guid fileId, CancellationToken cancellationToken);

    Task<ApiResponse<Guid>> ReplaceContentAsync(Guid fileId, byte[] content, CancellationToken cancellationToken);

    Task<ApiResponse<Guid>> ApplySystemMetadataAsync(Guid fileId, FileSystemMetadataDto metadata, CancellationToken cancellationToken);
}
