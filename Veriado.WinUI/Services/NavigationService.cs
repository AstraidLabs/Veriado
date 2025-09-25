using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Veriado.WinUI.Navigation;
using Veriado.WinUI.Services.Abstractions;
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
    }

    public void Navigate(PageId pageId)
    {
        if (_host is null)
        {
            throw new InvalidOperationException("Navigation host has not been attached.");
        }

        var registration = ResolveRegistration(pageId);
        var view = _serviceProvider.GetRequiredService(registration.ViewType);
        object? viewModel = null;

        if (registration.ViewModelType is not null)
        {
            viewModel = _serviceProvider.GetRequiredService(registration.ViewModelType);
        }

        if (view is FrameworkElement frameworkElement && viewModel is not null)
        {
            frameworkElement.DataContext = viewModel;
        }

        _host.CurrentContent = view;
    }

    private NavigationRegistration ResolveRegistration(PageId pageId)
    {
        if (_registrations.TryGetValue(pageId, out var registration))
        {
            return registration;
        }

        throw new ArgumentOutOfRangeException(nameof(pageId), pageId, "Unknown navigation target.");
    }

    private sealed record NavigationRegistration(Type ViewType, Type? ViewModelType);
}
