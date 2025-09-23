using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Veriado.Services.Abstractions;

namespace Veriado.Services;

public sealed class DialogService : IDialogService
{
    private readonly IWindowProvider _window;

    public DialogService(IWindowProvider window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", string cancelText = "Cancel")
    {
        var window = _window.GetActiveWindow();
        var hwnd = _window.GetHwnd(window);
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = confirmText,
            CloseButtonText = string.IsNullOrWhiteSpace(cancelText) ? null : cancelText,
            XamlRoot = _window.GetXamlRoot(window),
        };

        WinRT.Interop.InitializeWithWindow.Initialize(dialog, hwnd);
        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    public Task ShowInfoAsync(string title, string message)
    {
        return ConfirmAsync(title, message, "OK", string.Empty);
    }

    public Task ShowErrorAsync(string title, string message)
    {
        return ConfirmAsync(title, message, "OK", string.Empty);
    }
}
