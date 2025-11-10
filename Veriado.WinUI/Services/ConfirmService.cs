using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services;

public sealed class ConfirmService : IConfirmService
{
    private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(8);

    private readonly IWindowProvider _windowProvider;
    private readonly ILogger<ConfirmService> _logger;

    public ConfirmService(IWindowProvider windowProvider, ILogger<ConfirmService> logger)
    {
        _windowProvider = windowProvider ?? throw new ArgumentNullException(nameof(windowProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> TryConfirmAsync(
        string title,
        string message,
        string confirmText,
        string cancelText,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_windowProvider.TryGetWindow(out var window) || window?.Content is not FrameworkElement root)
            {
                _logger.LogDebug("Confirmation dialog skipped because no active window content is available.");
                return true;
            }

            var xamlRoot = root.XamlRoot;
            if (xamlRoot is null)
            {
                _logger.LogWarning("Confirmation dialog skipped because XamlRoot is not available.");
                return true;
            }

            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = confirmText,
                CloseButtonText = string.IsNullOrWhiteSpace(cancelText) ? null : cancelText,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot,
                RequestedTheme = root.ActualTheme,
            };

            using var timeoutCts = new CancellationTokenSource(DialogTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

            try
            {
                var result = await ShowDialogAsync(dialog, linkedCts.Token).ConfigureAwait(true);
                return result;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                _logger.LogWarning("Confirmation dialog timed out after {Timeout}.", DialogTimeout);
                return true;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Confirmation dialog canceled via token; allowing shutdown to proceed.");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Confirmation dialog failed. Proceeding with shutdown.");
            return true;
        }
    }

    private static async Task<bool> ShowDialogAsync(ContentDialog dialog, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(() =>
        {
            if (dialog.DispatcherQueue.HasThreadAccess)
            {
                if (dialog.IsLoaded)
                {
                    dialog.Hide();
                }
            }
            else
            {
                _ = dialog.DispatcherQueue.TryEnqueue(() =>
                {
                    if (dialog.IsLoaded)
                    {
                        dialog.Hide();
                    }
                });
            }
        });

        var result = await dialog.ShowAsync().AsTask().ConfigureAwait(true);
        if (cancellationToken.IsCancellationRequested)
        {
            return true;
        }

        return result == ContentDialogResult.Primary;
    }
}
