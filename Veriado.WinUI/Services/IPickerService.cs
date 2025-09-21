// BEGIN CHANGE Veriado.WinUI/Services/IPickerService.cs
using System.Threading;
using System.Threading.Tasks;

namespace Veriado.WinUI.Services;

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
}
// END CHANGE Veriado.WinUI/Services/IPickerService.cs
