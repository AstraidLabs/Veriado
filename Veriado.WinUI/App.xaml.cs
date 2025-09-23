using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Veriado.WinUI.Views;

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
