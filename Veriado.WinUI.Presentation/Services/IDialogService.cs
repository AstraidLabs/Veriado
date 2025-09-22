using System.Threading;
using System.Threading.Tasks;

namespace Veriado.Presentation.Services;

/// <summary>
/// Provides dialog presentation capabilities to view models.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Displays an informational dialog.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">The dialog message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken);

    /// <summary>
    /// Displays a confirmation dialog returning the user's choice.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">The dialog message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the user confirms; otherwise <see langword="false"/>.</returns>
    Task<bool> ShowConfirmationAsync(string title, string message, CancellationToken cancellationToken);
}
