using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Veriado.Application.DependencyInjection;
using Veriado.Infrastructure.DependencyInjection;
using Veriado.Mapping.DependencyInjection;
using Veriado.Services.DependencyInjection;
using Veriado.WinUI.DependencyInjection;
using Veriado.WinUI.Services.Pickers;
using Veriado.WinUI.ViewModels;
using Veriado.WinUI.ViewModels.Files;
using Veriado.WinUI.ViewModels.Import;

namespace Veriado;

internal sealed class AppHost : IAsyncDisposable
{
    private readonly IHost _host;

    private AppHost(IHost host)
    {
        _host = host;
    }

    public IServiceProvider Services => _host.Services;

    public static async Task<AppHost> StartAsync()
    {
        var host = Host
            .CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                var messenger = WeakReferenceMessenger.Default;
                services.AddSingleton(messenger);
                services.AddSingleton<IMessenger>(messenger);

                services.AddSingleton<ShellViewModel>();
                services.AddSingleton<SearchOverlayViewModel>();
                services.AddSingleton<FilesGridViewModel>();
                services.AddTransient<FileDetailViewModel>();
                services.AddSingleton<ImportViewModel>();

                services.AddSingleton<IPickerService, WinUIPickerService>();

                services.AddWinUiShell();

                services.AddApplication();
                services.AddVeriadoMapping();
                services.AddInfrastructure();
                services.AddVeriadoServices();

                // TODO: Configure infrastructure options (database path, etc.) when wiring the full stack.
            })
            .Build();

        await host.StartAsync().ConfigureAwait(false);
        await host.Services.InitializeInfrastructureAsync().ConfigureAwait(false);
        return new AppHost(host);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync().ConfigureAwait(false);
        _host.Dispose();
    }
}
