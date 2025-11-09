using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.ViewModels.Base;

namespace Veriado.WinUI.Services;

public sealed class DialogService : IDialogService
{
    private readonly IWindowProvider _window;
    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReadOnlyList<IDialogViewFactory> _factories;

    public DialogService(
        IWindowProvider window,
        IServiceProvider serviceProvider,
        IEnumerable<IDialogViewFactory> factories,
        IServiceScopeFactory scopeFactory)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _factories = factories?.ToArray() ?? throw new ArgumentNullException(nameof(factories));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public DialogViewModelScope<TViewModel> CreateViewModel<TViewModel>() where TViewModel : class
    {
        var scope = _scopeFactory.CreateAsyncScope();
        var viewModel = scope.ServiceProvider.GetRequiredService<TViewModel>();
        return new DialogViewModelScope<TViewModel>(scope, viewModel);
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", string cancelText = "Cancel")
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = confirmText,
            CloseButtonText = string.IsNullOrWhiteSpace(cancelText) ? null : cancelText,
        };

        var result = await ShowDialogAsync(dialog).ConfigureAwait(false);
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

    public async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        var window = _window.GetActiveWindow();
        if (window.Content is not FrameworkElement rootElement)
        {
            throw new InvalidOperationException("Cannot resolve XamlRoot from window.");
        }

        var xamlRoot = rootElement.XamlRoot
            ?? throw new InvalidOperationException("Cannot resolve XamlRoot from window.");

        dialog.XamlRoot = xamlRoot;
        dialog.RequestedTheme = rootElement.ActualTheme;

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

        return await dialog.ShowAsync().AsTask().ConfigureAwait(false);
    }

    private async Task<DialogResult> ShowDialogInternalAsync(ContentDialog dialog, object? viewModel, CancellationToken cancellationToken)
    {
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

        var dialogAware = viewModel as IDialogAware;

        if (dialogAware is not null)
        {
            dialogAware.CloseRequested += RequestClose;
        }

        using var registration = cancellationToken.Register(() =>
        {
            requestedResult = DialogResult.Canceled();
        });

        try
        {
            var result = await ShowDialogAsync(dialog, cancellationToken).ConfigureAwait(false);

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
            if (dialogAware is not null)
            {
                dialogAware.CloseRequested -= RequestClose;
            }
        }
    }
}
