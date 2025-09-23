using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.Views;
using Veriado.WinUI.ViewModels.Settings;

namespace Veriado;

public partial class App : Application
{
    private AppHost? _appHost;

    public App()
    {
        InitializeComponent();
    }

    public static new App Current => (App)Application.Current;

    public static IServiceProvider Services => Current.Services;

    public IServiceProvider Services => _appHost?.Services
        ?? throw new InvalidOperationException("Application host has not been started.");

    public Window MainWindow { get; private set; } = default!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);

        _appHost = AppHost.StartAsync().GetAwaiter().GetResult();

        MainWindow = Services.GetRequiredService<MainWindow>();
        var windowProvider = Services.GetRequiredService<IWindowProvider>();
        windowProvider.SetWindow(MainWindow);
        var keyboardShortcuts = Services.GetRequiredService<IKeyboardShortcutsService>();
        keyboardShortcuts.RegisterDefaultShortcuts();
        var themeService = Services.GetRequiredService<IThemeService>();
        themeService.InitializeAsync().GetAwaiter().GetResult();
        var settingsViewModel = Services.GetRequiredService<SettingsViewModel>();
        settingsViewModel.SelectedTheme = themeService.CurrentTheme;
        MainWindow.Closed += OnWindowClosed;
        MainWindow.Activate();
    }

    private async void OnWindowClosed(object sender, WindowEventArgs e)
    {
        if (_appHost is not null)
        {
            await _appHost.DisposeAsync().ConfigureAwait(false);
            _appHost = null;
        }

        MainWindow = null!;
    }
}
