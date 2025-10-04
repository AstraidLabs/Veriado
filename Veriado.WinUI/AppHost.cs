using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Veriado.Infrastructure.DependencyInjection;
using Veriado.Infrastructure.Persistence.Options;
using Veriado.Mapping.DependencyInjection;
using Veriado.Services;
using Veriado.Services.DependencyInjection;
using Veriado.WinUI.Services;
using Veriado.WinUI.ViewModels.Files;
using Veriado.WinUI.ViewModels.Import;
using Veriado.WinUI.ViewModels.Settings;
using Veriado.WinUI.ViewModels.Shell;
using Veriado.Appl.DependencyInjection;
using Veriado.WinUI.DependencyInjection;

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
            .ConfigureServices((context, services) =>
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
                services.AddSingleton<IClipboardService, ClipboardService>();
                services.AddSingleton<IShareService, ShareService>();
                services.AddSingleton<IPreviewService, PreviewService>();
                services.AddSingleton<ICacheService, MemoryCacheService>();
                services.AddSingleton<IHotStateService, HotStateService>();
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<IDialogService, DialogService>();
                services.AddSingleton<IPickerService, PickerService>();
                services.AddSingleton<IStatusService, StatusService>();

                services.AddSingleton<MainShellViewModel>();
                services.AddTransient<FilesPageViewModel>();
                services.AddTransient<ImportPageViewModel>();
                services.AddTransient<SettingsPageViewModel>();

                services.AddWinUiShell();

                services.AddVeriadoMapping();
                services.AddInfrastructure(context.Configuration, options =>
                {
                    var databasePath = infrastructureConfig.GetDatabasePath();
                    infrastructureConfig.EnsureStorageExists(databasePath);
                    options.DbPath = databasePath;
                    options.FtsIndexingMode = FtsIndexingMode.Outbox;
                });
                services.AddApplication();
                services.AddVeriadoServices();
                services.AddSingleton<IFilesSearchSuggestionsProvider, FilesSearchSuggestionsProvider>();
            })
            .Build();

        var configProvider = host.Services.GetRequiredService<IInfrastructureConfigProvider>();
        var databasePath = configProvider.GetDatabasePath();
        var mutexKey = BuildMigrationMutexKey(databasePath);

        using (var gate = new NamedGlobalMutex(mutexKey))
        {
            gate.Acquire(TimeSpan.FromSeconds(30));
            await host.Services.InitializeInfrastructureAsync().ConfigureAwait(false);
        }
        await host.StartAsync().ConfigureAwait(false);
        await host.Services.GetRequiredService<IHotStateService>().InitializeAsync().ConfigureAwait(false);
        return new AppHost(host);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync().ConfigureAwait(false);
        _host.Dispose();
    }

    private static string BuildMigrationMutexKey(string databasePath)
    {
        var sanitized = databasePath
            .Replace(Path.DirectorySeparatorChar, '_')
            .Replace(Path.AltDirectorySeparatorChar, '_')
            .Replace(':', '_');
        return $"Veriado-Migrate-{sanitized}";
    }
}

internal sealed class NamedGlobalMutex : IDisposable
{
    private readonly Mutex _mutex;
    private bool _hasHandle;

    public NamedGlobalMutex(string name)
    {
        var mutexName = OperatingSystem.IsWindows() ? @$"Global\{name}" : name;
        _mutex = new Mutex(false, mutexName);
    }

    public void Acquire(TimeSpan timeout)
    {
        try
        {
            _hasHandle = _mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            _hasHandle = true;
        }

        if (!_hasHandle)
        {
            throw new TimeoutException("Nelze získat migrační mutex – pravděpodobně běží jiná instance.");
        }
    }

    public void Dispose()
    {
        if (_hasHandle)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
