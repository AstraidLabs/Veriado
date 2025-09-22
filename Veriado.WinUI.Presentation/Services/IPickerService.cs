// BEGIN CHANGE Veriado.Presentation/Services/IPickerService.cs
using System.Threading;
using System.Threading.Tasks;

namespace Veriado.Presentation.Services;

/// <summary>
/// Abstraction over platform pickers used to select folders or files.
/// </summary>
public interface IPickerService
{
    /// <summary>
    /// Prompts the user to select a folder.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The absolute folder path when selection succeeds; otherwise <see langword="null"/>.</returns>
    Task<string?> PickFolderAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Prompts the user to select a single file and returns its content.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The picked file or <see langword="null"/> when the selection is cancelled.</returns>
    Task<PickedFile?> PickFileAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Represents a picked file.
/// </summary>
/// <param name="Name">The file name.</param>
/// <param name="Content">The file content.</param>
/// <param name="ContentType">The MIME content type when known.</param>
public sealed record PickedFile(string Name, byte[] Content, string? ContentType);
// END CHANGE Veriado.Presentation/Services/IPickerService.cs
