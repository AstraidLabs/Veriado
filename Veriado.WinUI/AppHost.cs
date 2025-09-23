using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Veriado.Application.DependencyInjection;
using Veriado.Converters;
using Veriado.Infrastructure.DependencyInjection;
using Veriado.Mapping.DependencyInjection;
using Veriado.Presentation.Services;
using Veriado.Services;
using Veriado.Services.DependencyInjection;
using Veriado.ViewModels.Files;
using Veriado.ViewModels.Import;
using Veriado.ViewModels.Search;
using Veriado.ViewModels.Shell;

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
                services.AddSingleton<ShellViewModel>();
                services.AddTransient<FilesGridViewModel>();
                services.AddTransient<FileDetailViewModel>();
                services.AddTransient<ImportViewModel>();
                services.AddTransient<HistoryViewModel>();
                services.AddTransient<FavoritesViewModel>();

                services.AddSingleton<BoolToSeverityConverter>();

                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<IPickerService, PickerService>();

                services.AddApplication();
                services.AddVeriadoMapping();
                services.AddInfrastructure();
                services.AddVeriadoServices();

                // TODO: Configure infrastructure options (database path, etc.) when wiring the full stack.
            })
            .Build();

        await host.StartAsync().ConfigureAwait(false);
        return new AppHost(host);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync().ConfigureAwait(false);
        _host.Dispose();
    }
}
