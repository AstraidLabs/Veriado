using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services;

public sealed class ConfirmService : IConfirmService
{
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
        ConfirmOptions? options = null)
    {
        var effectiveOptions = options ?? new ConfirmOptions();
        var timeout = effectiveOptions.Timeout;
        var callerToken = effectiveOptions.CancellationToken;

        try
        {
            if (!_windowProvider.TryGetWindow(out var window) || window?.Content is not FrameworkElement root)
            {
                _logger.LogWarning("Confirmation dialog unavailable because no active window content is present.");
                return false;
            }

            var xamlRoot = root.XamlRoot;
            if (xamlRoot is null)
            {
                _logger.LogWarning("Confirmation dialog skipped because XamlRoot is not available.");
                return false;
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

            using var timeoutCts = timeout == Timeout.InfiniteTimeSpan
                ? null
                : new CancellationTokenSource(timeout);

            CancellationToken combinedToken;
            CancellationTokenSource? linkedCts = null;
            if (timeoutCts is not null && callerToken.CanBeCanceled)
            {
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, callerToken);
                combinedToken = linkedCts.Token;
            }
            else if (timeoutCts is not null)
            {
                combinedToken = timeoutCts.Token;
            }
            else
            {
                combinedToken = callerToken;
            }

            try
            {
                return await ShowDialogAsync(dialog, timeoutCts, callerToken, combinedToken).ConfigureAwait(true);
            }
            finally
            {
                linkedCts?.Dispose();
            }
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            _logger.LogInformation("Confirmation dialog canceled via caller token; treating as rejection.");
            return false;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Confirmation dialog timed out after {Timeout}.", timeout);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Confirmation dialog failed. Rejecting shutdown to keep application open.");
            return false;
        }
    }

    private static async Task<bool> ShowDialogAsync(
        ContentDialog dialog,
        CancellationTokenSource? timeoutCts,
        CancellationToken callerToken,
        CancellationToken combinedToken)
    {
        using var registration = combinedToken.Register(() =>
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
        if (combinedToken.IsCancellationRequested)
        {
            return false;
        }

        if (timeoutCts is not null && timeoutCts.IsCancellationRequested)
        {
            return false;
        }

        if (callerToken.IsCancellationRequested)
        {
            return false;
        }

        return result == ContentDialogResult.Primary;
    }
}
