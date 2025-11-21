// File: Veriado.Services/Files/IFileContentService.cs
namespace Veriado.Services.Files;

/// <summary>
/// Provides helpers for accessing and persisting file binary content.
/// </summary>
public interface IFileContentService
{
    Task<FileContentResponseDto?> GetContentAsync(Guid fileId, CancellationToken cancellationToken);

    Task<AppResult<Guid>> SaveContentToDiskAsync(Guid fileId, string targetPath, CancellationToken cancellationToken);
}
