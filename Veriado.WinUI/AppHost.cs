using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Veriado.WinUI.DependencyInjection;
using Veriado.Infrastructure.DependencyInjection;
using Veriado.Infrastructure.Persistence.Options;
using Veriado.Mapping.DependencyInjection;
using Veriado.Services;
using Veriado.WinUI.Services.Abstractions;
using Veriado.Services.DependencyInjection;
using Veriado.WinUI.Services;
using Veriado.WinUI.ViewModels;
using Veriado.WinUI.ViewModels.Files;
using Veriado.WinUI.ViewModels.Import;
using Veriado.WinUI.ViewModels.Search;
using Veriado.WinUI.ViewModels.Settings;
using Veriado.Appl.DependencyInjection;

namespace Veriado.WinUI;

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

                var infrastructureConfig = new InfrastructureConfigProvider();
                services.AddSingleton<IInfrastructureConfigProvider>(infrastructureConfig);
                services.AddSingleton<InfrastructureConfigProvider>(infrastructureConfig);

                services.AddSingleton<IWindowProvider, WindowProvider>();
                services.AddSingleton<ISettingsService, JsonSettingsService>();
                services.AddSingleton<IDispatcherService, DispatcherService>();
                services.AddSingleton<IThemeService, ThemeService>();
                services.AddSingleton<IExceptionHandler, ExceptionHandler>();
                services.AddSingleton<IKeyboardShortcutsService, KeyboardShortcutsService>();
                services.AddSingleton<IClipboardService, ClipboardService>();
                services.AddSingleton<IShareService, ShareService>();
                services.AddSingleton<IPreviewService, PreviewService>();
                services.AddSingleton<ICacheService, MemoryCacheService>();
                services.AddSingleton<IHotStateService, HotStateService>();
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<IDialogService, DialogService>();
                services.AddSingleton<IPickerService, PickerService>();
                services.AddSingleton<IStatusService, StatusService>();

                services.AddSingleton<ShellViewModel>();
                services.AddSingleton<SearchOverlayViewModel>();
                services.AddTransient<FilesGridViewModel>();
                services.AddSingleton<FiltersNavViewModel>();
                services.AddTransient<FileDetailViewModel>();
                services.AddTransient<ImportViewModel>();
                services.AddSingleton<FavoritesViewModel>();
                services.AddSingleton<HistoryViewModel>();
                services.AddSingleton<SettingsViewModel>();

                services.AddWinUiShell();

                services.AddVeriadoMapping();
                services.AddInfrastructure(options =>
                {
                    var databasePath = infrastructureConfig.GetDatabasePath();
                    infrastructureConfig.EnsureStorageExists(databasePath);
                    options.DbPath = databasePath;
                    options.UseKvMetadata = true;
                    options.FtsIndexingMode = FtsIndexingMode.Outbox;
                });
                services.AddApplication();
                services.AddVeriadoServices();
                services.AddSingleton<IFilesSearchSuggestionsProvider, FilesSearchSuggestionsProvider>();
            })
            .Build();

        await host.StartAsync().ConfigureAwait(false);
        await host.Services.InitializeInfrastructureAsync().ConfigureAwait(false);
        await host.Services.GetRequiredService<IHotStateService>().InitializeAsync().ConfigureAwait(false);
        return new AppHost(host);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync().ConfigureAwait(false);
        _host.Dispose();
    }
}
