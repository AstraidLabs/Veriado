using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.Helpers;
using Veriado.WinUI.Navigation;
using Veriado.WinUI.ViewModels.Files;
using Veriado.WinUI.ViewModels.Import;
using Veriado.WinUI.ViewModels.Settings;
using Veriado.WinUI.Views.Files;
using Veriado.WinUI.Views.Import;
using Veriado.WinUI.Views.Settings;

namespace Veriado.WinUI.Services;

public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IReadOnlyDictionary<PageId, NavigationRegistration> _registrations;
    private INavigationHost? _host;
    private Frame? _frame;

    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _registrations = new Dictionary<PageId, NavigationRegistration>
        {
            [PageId.Files] = new NavigationRegistration(typeof(FilesPage), typeof(FilesPageViewModel)),
            [PageId.Import] = new NavigationRegistration(typeof(ImportPage), typeof(ImportPageViewModel)),
            [PageId.Settings] = new NavigationRegistration(typeof(SettingsPage), typeof(SettingsPageViewModel)),
        };
    }

    public void AttachHost(INavigationHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _frame = _host.NavigationFrame ?? throw new InvalidOperationException("Navigation host frame is not available.");

        NavigationAnimations.Attach(_frame, AnimationSettings.AreEnabled);
        AnimationSettings.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;
        AnimationSettings.AnimationsEnabledChanged += OnAnimationsEnabledChanged;
    }

    public void Navigate(PageId pageId)
    {
        if (_frame is null)
        {
            throw new InvalidOperationException("Navigation host has not been attached.");
        }

        var registration = ResolveRegistration(pageId);
        var navigated = _frame.Navigate(registration.ViewType);

        if (!navigated)
        {
            throw new InvalidOperationException($"Failed to navigate to page '{registration.ViewType.FullName}'.");
        }

        if (_frame.Content is FrameworkElement element)
        {
            var viewModel = ResolveViewModel(element, registration);
            if (viewModel is not null && !ReferenceEquals(element.DataContext, viewModel))
            {
                element.DataContext = viewModel;
            }
        }
    }

    private NavigationRegistration ResolveRegistration(PageId pageId)
    {
        if (_registrations.TryGetValue(pageId, out var registration))
        {
            return registration;
        }

        throw new ArgumentOutOfRangeException(nameof(pageId), pageId, "Unknown navigation target.");
    }

    private object? ResolveViewModel(FrameworkElement view, NavigationRegistration registration)
    {
        if (registration.ViewModelType is null)
        {
            return null;
        }

        if (view.DataContext is not null && registration.ViewModelType.IsInstanceOfType(view.DataContext))
        {
            return view.DataContext;
        }

        return _serviceProvider.GetRequiredService(registration.ViewModelType);
    }

    private void OnAnimationsEnabledChanged(object? sender, bool enabled)
    {
        if (_frame?.DispatcherQueue is null)
        {
            return;
        }

        _ = _frame.DispatcherQueue.TryEnqueue(() => NavigationAnimations.Attach(_frame, enabled));
    }

    private sealed record NavigationRegistration(Type ViewType, Type? ViewModelType);
}
