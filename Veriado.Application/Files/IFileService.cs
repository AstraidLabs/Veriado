using Veriado.Application.Files.Contracts;

namespace Veriado.Application.Files;

/// <summary>
/// Exposes application-level operations for working with editable file details.
/// </summary>
public interface IFileService
{
    Task<FileDetailDto> GetDetailAsync(Guid id, CancellationToken cancellationToken);

    Task UpdateAsync(FileDetailDto detail, CancellationToken cancellationToken);
}
