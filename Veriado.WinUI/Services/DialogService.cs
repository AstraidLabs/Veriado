using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.ViewModels.Base;

namespace Veriado.WinUI.Services;

public sealed class DialogService : IDialogService
{
    private readonly IWindowProvider _window;
    private readonly IServiceProvider _serviceProvider;
    private readonly IReadOnlyList<IDialogViewFactory> _factories;

    public DialogService(IWindowProvider window, IServiceProvider serviceProvider, IEnumerable<IDialogViewFactory> factories)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _factories = factories?.ToArray() ?? throw new ArgumentNullException(nameof(factories));
    }

    public TViewModel CreateViewModel<TViewModel>() where TViewModel : class
        => _serviceProvider.GetRequiredService<TViewModel>();

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

    public async Task<DialogResult> ShowDialogAsync<TViewModel>(TViewModel viewModel, CancellationToken cancellationToken = default)
        where TViewModel : class
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        var dialog = ResolveDialog(viewModel);
        return await ShowDialogInternalAsync(dialog, viewModel, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DialogResult> ShowDialogAsync(DialogRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Content);

        var dialog = new ContentDialog
        {
            Title = request.Title,
            Content = request.Content,
            PrimaryButtonText = request.PrimaryButtonText,
            SecondaryButtonText = request.SecondaryButtonText,
            CloseButtonText = request.CloseButtonText,
            DefaultButton = request.DefaultButton,
        };

        return await ShowDialogInternalAsync(dialog, null, cancellationToken).ConfigureAwait(false);
    }

    private ContentDialog ResolveDialog(object viewModel)
    {
        foreach (var factory in _factories)
        {
            if (factory.CanCreate(viewModel))
            {
                return factory.Create(viewModel);
            }
        }

        throw new InvalidOperationException($"No dialog factory registered for view model type '{viewModel.GetType().FullName}'.");
    }

    private async Task<DialogResult> ShowDialogInternalAsync(ContentDialog dialog, object? viewModel, CancellationToken cancellationToken)
    {
        var window = _window.GetActiveWindow();
        var hwnd = _window.GetHwnd(window);
        dialog.XamlRoot = _window.GetXamlRoot(window);

        WinRT.Interop.InitializeWithWindow.Initialize(dialog, hwnd);

        DialogResult? requestedResult = null;

        void RequestClose(object? sender, DialogResult result)
        {
            requestedResult = result;
            _ = dialog.DispatcherQueue.TryEnqueue(() =>
            {
                if (dialog.IsLoaded)
                {
                    dialog.Hide();
                }
            });
        }

        if (viewModel is IDialogAware aware)
        {
            aware.CloseRequested += RequestClose;
        }

        using var registration = cancellationToken.Register(() =>
        {
            requestedResult = DialogResult.Canceled();
            _ = dialog.DispatcherQueue.TryEnqueue(() =>
            {
                if (dialog.IsLoaded)
                {
                    dialog.Hide();
                }
            });
        });

        try
        {
            var result = await dialog.ShowAsync();

            if (cancellationToken.IsCancellationRequested)
            {
                return DialogResult.Canceled();
            }

            if (requestedResult is { } custom)
            {
                return custom;
            }

            var wasCloseButton = result == ContentDialogResult.None && !string.IsNullOrWhiteSpace(dialog.CloseButtonText);
            return DialogResult.From(result, wasCloseButton);
        }
        finally
        {
            if (viewModel is IDialogAware aware)
            {
                aware.CloseRequested -= RequestClose;
            }
        }
    }
}
