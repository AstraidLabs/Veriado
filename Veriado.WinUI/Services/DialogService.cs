using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Veriado.WinUI.Services;

/// <summary>
/// WinUI implementation of <see cref="IDialogService"/> using <see cref="ContentDialog"/>.
/// </summary>
public sealed class DialogService : IDialogService
{
    /// <inheritdoc />
    public async Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken)
    {
        var dialog = CreateDialog(title, message);
        dialog.PrimaryButtonText = "OK";

        await dialog.ShowAsync(ContentDialogPlacement.Popup);
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <inheritdoc />
    public async Task<bool> ShowConfirmationAsync(string title, string message, CancellationToken cancellationToken)
    {
        var dialog = CreateDialog(title, message);
        dialog.PrimaryButtonText = "Ano";
        dialog.SecondaryButtonText = "Ne";

        var result = await dialog.ShowAsync(ContentDialogPlacement.Popup);
        cancellationToken.ThrowIfCancellationRequested();
        return result == ContentDialogResult.Primary;
    }

    private static ContentDialog CreateDialog(string title, string message)
    {
        if (App.MainWindowInstance?.Content is not FrameworkElement root)
        {
            throw new InvalidOperationException("Main window content is not available for dialog hosting.");
        }

        return new ContentDialog
        {
            Title = title,
            Content = message,
            XamlRoot = root.XamlRoot,
        };
    }
}
