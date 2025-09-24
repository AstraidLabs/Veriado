using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services;

public sealed class ThemeService : IThemeService
{
    private readonly ISettingsService _settingsService;
    private readonly IWindowProvider _windowProvider;
    private readonly IDispatcherService _dispatcherService;
    private AppTheme _currentTheme = AppTheme.Default;

    public ThemeService(
        ISettingsService settingsService,
        IWindowProvider windowProvider,
        IDispatcherService dispatcherService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _windowProvider = windowProvider ?? throw new ArgumentNullException(nameof(windowProvider));
        _dispatcherService = dispatcherService ?? throw new ArgumentNullException(nameof(dispatcherService));
    }

    public AppTheme CurrentTheme => _currentTheme;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetAsync(cancellationToken).ConfigureAwait(false);
        _currentTheme = settings.Theme;
        await ApplyThemeAsync(_currentTheme, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetThemeAsync(AppTheme theme, CancellationToken cancellationToken = default)
    {
        _currentTheme = theme;
        await ApplyThemeAsync(theme, cancellationToken).ConfigureAwait(false);
        await _settingsService.UpdateAsync(settings => settings.Theme = theme, cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyThemeAsync(AppTheme theme, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (_dispatcherService.HasThreadAccess)
        {
            ApplyThemeCore(theme);
            return;
        }

        await _dispatcherService.Enqueue(() =>
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                ApplyThemeCore(theme);
            }
        }).ConfigureAwait(false);
    }

    private void ApplyThemeCore(AppTheme theme)
    {
        if (!_windowProvider.TryGetWindow(out var window) || window?.Content is not FrameworkElement root)
        {
            return;
        }

        root.RequestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }
}
