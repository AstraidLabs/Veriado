using System;
using Veriado.Services.Abstractions;

namespace Veriado.Services;

public sealed class NavigationService : INavigationService
{
    private INavigationHost? _host;

    public void AttachHost(INavigationHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public void NavigateTo(object view, object? viewModel = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (_host is null)
        {
            throw new InvalidOperationException("Navigation host has not been attached.");
        }

        if (view is Microsoft.UI.Xaml.FrameworkElement frameworkElement && viewModel is not null)
        {
            frameworkElement.DataContext = viewModel;
        }

        _host.CurrentDetail = null;
        _host.CurrentContent = view;
    }

    public void NavigateDetail(object view, object? viewModel = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (_host is null)
        {
            throw new InvalidOperationException("Navigation host has not been attached.");
        }

        if (view is Microsoft.UI.Xaml.FrameworkElement frameworkElement && viewModel is not null)
        {
            frameworkElement.DataContext = viewModel;
        }

        _host.CurrentDetail = view;
    }
}
