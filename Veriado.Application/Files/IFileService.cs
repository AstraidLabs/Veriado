using Veriado.Appl.Files.Contracts;

namespace Veriado.Appl.Files;

/// <summary>
/// Exposes application-level operations for working with editable file details.
/// </summary>
public interface IFileService
{
    Task<EditableFileDetailDto> GetDetailAsync(Guid id, CancellationToken cancellationToken);

    Task UpdateAsync(EditableFileDetailDto detail, CancellationToken cancellationToken);
}
