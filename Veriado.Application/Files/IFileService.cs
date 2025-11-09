using Veriado.Appl.Files.Contracts;

namespace Veriado.Appl.Files;

/// <summary>
/// Exposes application-level operations for working with editable file details.
/// </summary>
public interface IFileService
{
    Task<EditableFileDetailDto> GetDetailAsync(Guid id, CancellationToken cancellationToken);

    Task<EditableFileDetailDto> UpdateAsync(EditableFileDetailDto detail, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
