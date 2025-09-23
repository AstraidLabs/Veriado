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
    private AppTheme _currentTheme = AppTheme.Default;

    public ThemeService(ISettingsService settingsService, IWindowProvider windowProvider)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _windowProvider = windowProvider ?? throw new ArgumentNullException(nameof(windowProvider));
    }

    public AppTheme CurrentTheme => _currentTheme;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetAsync(cancellationToken).ConfigureAwait(false);
        _currentTheme = settings.Theme;
        ApplyTheme(_currentTheme);
    }

    public async Task SetThemeAsync(AppTheme theme, CancellationToken cancellationToken = default)
    {
        _currentTheme = theme;
        ApplyTheme(theme);
        await _settingsService.UpdateAsync(settings => settings.Theme = theme, cancellationToken).ConfigureAwait(false);
    }

    private void ApplyTheme(AppTheme theme)
    {
        var window = _windowProvider.TryGetWindow();
        if (window?.Content is not FrameworkElement root)
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
