using System.IO;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Veriado.Appl.DependencyInjection;
using Veriado.Infrastructure.DependencyInjection;
using Veriado.Infrastructure.Persistence.Options;
using Veriado.Mapping.DependencyInjection;
using Veriado.Services;
using Veriado.Services.DependencyInjection;
using Veriado.WinUI.DependencyInjection;
using Veriado.WinUI.Services;
using Veriado.WinUI.ViewModels.Files;
using Veriado.WinUI.ViewModels.Import;
using Veriado.WinUI.ViewModels.Settings;
using Veriado.WinUI.ViewModels.Shell;

namespace Veriado.WinUI;

public static class AppHost
{
    private static IHost? _host;

    public static bool IsBuilt => _host is not null;

    public static IServiceProvider Services =>
        _host?.Services ?? throw new InvalidOperationException(
            "AppHost is not initialized. Call AppHost.Build() and StartAsync() before accessing Services.");

    public static void Build()
    {
        if (_host is not null)
        {
            return;
        }

        _host = Host
            .CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                var messenger = WeakReferenceMessenger.Default;
                services.AddSingleton(messenger);
                services.AddSingleton<IMessenger>(messenger);

                var infrastructureConfig = new InfrastructureConfigProvider();
                services.AddSingleton<IInfrastructureConfigProvider>(infrastructureConfig);
                services.AddSingleton(infrastructureConfig);

                services.AddSingleton<IWindowProvider, WindowProvider>();
                services.AddSingleton<ISettingsService, JsonSettingsService>();
                services.AddSingleton<ILocalizationService, LocalizationService>();
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
                    options.DbPath = databasePath;
                    options.FtsIndexingMode = FtsIndexingMode.Outbox;
                });
                services.AddApplication();
                services.AddVeriadoServices();
                services.AddSingleton<IFilesSearchSuggestionsProvider, FilesSearchSuggestionsProvider>();
            })
            .Build();
    }

    public static async Task StartAsync()
    {
        if (_host is null)
        {
            throw new InvalidOperationException("AppHost.Build() must be called before StartAsync().");
        }

        var configProvider = _host.Services.GetRequiredService<IInfrastructureConfigProvider>();
        var databasePath = configProvider.GetDatabasePath();
        var mutexKey = BuildMigrationMutexKey(databasePath);

        using (var gate = new NamedGlobalMutex(mutexKey))
        {
            gate.Acquire(TimeSpan.FromSeconds(30));
            await _host.Services.InitializeInfrastructureAsync().ConfigureAwait(false);
        }

        await _host.StartAsync().ConfigureAwait(false);
    }

    public static async Task StopAsync()
    {
        if (_host is null)
        {
            return;
        }

        await _host.StopAsync().ConfigureAwait(false);
        _host.Dispose();
        _host = null;
    }

    private static string BuildMigrationMutexKey(string databasePath)
    {
        var sanitized = databasePath
            .Replace(Path.DirectorySeparatorChar, '_')
            .Replace(Path.AltDirectorySeparatorChar, '_')
            .Replace(':', '_');
        return $"Veriado-Migrate-{sanitized}";
    }

    private sealed class NamedGlobalMutex : IDisposable
    {
        private readonly Mutex _mutex;
        private bool _hasHandle;

        public NamedGlobalMutex(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Mutex name must be provided.", nameof(name));
            }

            _mutex = TryCreateMutex(name) ?? new Mutex(false, name);
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

        private static Mutex? TryCreateMutex(string name)
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            foreach (var scope in new[] { "Global", "Local" })
            {
                try
                {
                    return new Mutex(false, @$"{scope}\{name}");
                }
                catch (UnauthorizedAccessException)
                {
                    // Fall back to the next scope if the current scope is not accessible.
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    // The target scope is unavailable – try the next one.
                }
            }

            return null;
        }
    }
}
