using System;
using Microsoft.UI.Xaml;

namespace Veriado;

public partial class App : Application
{
    private AppHost? _appHost;
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    public static new App Current => (App)Application.Current;

    public IServiceProvider Services => _appHost?.Services
        ?? throw new InvalidOperationException("Application host has not been started.");

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);

        _appHost = AppHost.StartAsync().GetAwaiter().GetResult();

        _window = new Views.MainWindow();
        _window.Closed += OnWindowClosed;
        _window.Activate();
    }

    private async void OnWindowClosed(object sender, WindowEventArgs e)
    {
        if (_appHost is not null)
        {
            await _appHost.DisposeAsync().ConfigureAwait(false);
            _appHost = null;
        }
    }
}
