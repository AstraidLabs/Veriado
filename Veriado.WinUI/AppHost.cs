using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veriado.Infrastructure.DependencyInjection;
using Veriado.Infrastructure.Persistence.Connections;
using Veriado.Infrastructure.Persistence.Options;
using Veriado.Mapping.DependencyInjection;
using Veriado.Services;
using Veriado.Services.DependencyInjection;
using Veriado.WinUI.Services;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.Services.DialogFactories;
using Veriado.WinUI.Services.Shutdown;
using Veriado.WinUI.ViewModels.Files;
using Veriado.WinUI.ViewModels.Import;
using Veriado.WinUI.ViewModels.Settings;
using Veriado.WinUI.ViewModels.Shell;
using Veriado.WinUI.Views.Files;
using Veriado.Appl.DependencyInjection;
using Veriado.WinUI.DependencyInjection;

namespace Veriado.WinUI;

internal sealed class AppHost : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly HostShutdownService _hostShutdownService;
    private readonly ILogger<AppHost> _logger;
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);
    private bool _disposed;

    private AppHost(IHost host, HostShutdownService hostShutdownService, ILogger<AppHost> logger)
    {
        _host = host;
        _hostShutdownService = hostShutdownService;
        _logger = logger;
    }

    public IServiceProvider Services => _host?.Services
        ?? throw new InvalidOperationException("Application host has not been started.");

    public static async Task<AppHost> StartAsync()
    {
        var host = Host
            .CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                var messenger = WeakReferenceMessenger.Default;
                services.AddSingleton(messenger);
                services.AddSingleton<IMessenger>(messenger);

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
                services.AddSingleton<IConfirmService, ConfirmService>();
                services.AddSingleton<HostShutdownService>();
                services.AddSingleton<IHostShutdownService>(static sp => sp.GetRequiredService<HostShutdownService>());
                services.AddSingleton<IShutdownOrchestrator, ShutdownOrchestrator>();
                services.AddSingleton<IStartupCoordinator, StartupCoordinator>();
                services.AddSingleton<ITimeFormattingService, TimeFormattingService>();
                services.AddSingleton<IServerClock, ServerClock>();
                services.AddSingleton<IPickerService, PickerService>();
                services.AddSingleton<IStatusService, StatusService>();

                services.AddSingleton<MainShellViewModel>();
                services.AddTransient<FileDetailDialogViewModel>();
                services.AddTransient<FilesPageViewModel>();
                services.AddTransient<ImportPageViewModel>();
                services.AddTransient<SettingsPageViewModel>();

                services.AddTransient<FileDetailDialog>();
                services.AddTransient<IDialogViewFactory, FileDetailDialogFactory>();

                services.AddWinUiShell();

                services.AddVeriadoMapping();
                services.AddInfrastructure(context.Configuration);
                services.AddApplication();
                services.AddVeriadoServices();
                services.AddSingleton<IFilesSearchSuggestionsProvider, FilesSearchSuggestionsProvider>();
            })
            .Build();

        var hostShutdownService = host.Services.GetRequiredService<HostShutdownService>();
        hostShutdownService.Initialize(host);

        var pathResolver = host.Services.GetRequiredService<ISqlitePathResolver>();
        var databasePath = pathResolver.Resolve(SqliteResolutionScenario.Runtime);
        pathResolver.EnsureStorageExists(databasePath);
        var mutexKey = BuildMigrationMutexKey(databasePath);

        using (var gate = new NamedGlobalMutex(mutexKey))
        {
            gate.Acquire(TimeSpan.FromSeconds(30));
            await host.StartAsync().ConfigureAwait(false);
        }
        var logger = host.Services.GetRequiredService<ILogger<AppHost>>();
        return new AppHost(host, hostShutdownService, logger);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (!_hostShutdownService.IsStopCompleted)
            {
                using var stopCts = new CancellationTokenSource(StopTimeout);
                try
                {
                    await _hostShutdownService.StopAsync(stopCts.Token).ConfigureAwait(false);
                    _logger.LogDebug("Host stopped successfully from AppHost.");
                }
                catch (OperationCanceledException)
                {
                    if (stopCts.IsCancellationRequested)
                    {
                        _logger.LogWarning("AppHost stop timed out after {Timeout}.", StopTimeout);
                    }
                    else
                    {
                        _logger.LogInformation("AppHost stop canceled via caller token.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AppHost failed to stop the host.");
                }
            }

            if (_hostShutdownService.IsDisposeCompleted)
            {
                _logger.LogDebug("AppHost skip dispose because host was already released by orchestrator.");
                return;
            }

            await _hostShutdownService.DisposeAsync().ConfigureAwait(false);
            _host = null;
            _logger.LogDebug("Host disposed successfully from AppHost.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AppHost failed during shutdown.");
        }
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
