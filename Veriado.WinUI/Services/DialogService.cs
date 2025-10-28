namespace Veriado.WinUI.Services;

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

    public Task ShowInfoAsync(string title, string message) => ConfirmAsync(title, message, "OK", string.Empty);

    public Task ShowErrorAsync(string title, string message) => ConfirmAsync(title, message, "OK", string.Empty);

    public async Task ShowAsync(string title, UIElement content, string primaryButtonText = "OK")
    {
        var request = new DialogRequest(title, content, primaryButtonText);
        await ShowDialogAsync(request).ConfigureAwait(false);
    }

    public async Task<DialogResult> ShowDialogAsync(DialogRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Content);

        var window = _window.GetActiveWindow();
        var hwnd = _window.GetHwnd(window);
        var xamlRoot = _window.GetXamlRoot(window);
        var dialog = new ContentDialog
        {
            Title = request.Title,
            Content = request.Content,
            PrimaryButtonText = request.PrimaryButtonText,
            SecondaryButtonText = request.SecondaryButtonText,
            CloseButtonText = request.CloseButtonText,
            DefaultButton = request.DefaultButton,
            XamlRoot = xamlRoot,
        };

        WinRT.Interop.InitializeWithWindow.Initialize(dialog, hwnd);

        using var registration = cancellationToken.Register(() =>
        {
            _ = dialog.DispatcherQueue.TryEnqueue(() =>
            {
                if (dialog.IsLoaded)
                {
                    dialog.Hide();
                }
            });
        });

        var result = await dialog.ShowAsync();

        if (cancellationToken.IsCancellationRequested)
        {
            return DialogResult.Canceled();
        }

        var wasCloseButton = result == ContentDialogResult.None && !string.IsNullOrWhiteSpace(request.CloseButtonText);
        return DialogResult.From(result, wasCloseButton);
    }
}
