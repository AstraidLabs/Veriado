// BEGIN CHANGE Veriado.WinUI/AppHost.cs
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Veriado.Application.DependencyInjection;
using Veriado.Infrastructure.DependencyInjection;
using Veriado.Mapping.DependencyInjection;
using Veriado.Services.DependencyInjection;
using Veriado.WinUI.DependencyInjection;
using Veriado.WinUI.Services;

namespace Veriado;

/// <summary>
/// Configures and manages the WinUI host environment.
/// </summary>
internal static class AppHost
{
    private static readonly SemaphoreSlim StartLock = new(1, 1);
    private static IHost? _host;

    public static IServiceProvider Services => _host?.Services
        ?? throw new InvalidOperationException("Host is not initialized.");

    public static async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_host is not null)
        {
            return;
        }

        await StartLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_host is null)
            {
                _host = BuildHost();
                await _host.Services.InitializeInfrastructureAsync(cancellationToken).ConfigureAwait(false);
                await _host.StartAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            StartLock.Release();
        }
    }

    public static async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_host is null)
        {
            return;
        }

        await _host.StopAsync(cancellationToken).ConfigureAwait(false);
        _host.Dispose();
        _host = null;
    }

    private static IHost BuildHost()
    {
        return Host
            .CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);

                services.AddApplication();
                services.AddVeriadoMapping();
                services.AddInfrastructure(options => options.DbPath = ResolveDatabasePath());
                services.AddVeriadoServices();

                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<IDialogService, DialogService>();
                services.AddSingleton<IPickerService, WinUIPickerService>();

                services.AddViewModels();

                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    private static string ResolveDatabasePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(appData, "Veriado");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "veriado.db");
    }
}
// END CHANGE Veriado.WinUI/AppHost.cs
