using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Veriado.Infrastructure.DependencyInjection;
using Veriado.Infrastructure.Persistence.Connections;
using Veriado.Infrastructure.Persistence.Options;
using Veriado.Mapping.DependencyInjection;
using Veriado.Services;
using Veriado.Services.DependencyInjection;
using Veriado.WinUI.Services;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.Services.DialogFactories;
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

    private AppHost(IHost host)
    {
        _host = host;
    }

    public IServiceProvider Services => _host.Services;

    public static async Task<AppHost> StartAsync(CancellationToken cancellationToken = default)
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

        cancellationToken.ThrowIfCancellationRequested();

        var pathResolver = host.Services.GetRequiredService<ISqlitePathResolver>();
        var databasePath = pathResolver.Resolve(SqliteResolutionScenario.Runtime);
        pathResolver.EnsureStorageExists(databasePath);
        var mutexKey = BuildMigrationMutexKey(databasePath);

        using (var gate = new NamedGlobalMutex(mutexKey))
        {
            await gate.AcquireAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        await host.Services
            .GetRequiredService<IHotStateService>()
            .InitializeAsync(cancellationToken)
            .ConfigureAwait(false);
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

    public async Task AcquireAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var acquired = TryAcquireOnce();
            if (acquired)
            {
                _hasHandle = true;
                return;
            }

            if (stopwatch.Elapsed >= timeout)
            {
                throw new TimeoutException("Nelze získat migrační mutex – pravděpodobně běží jiná instance.");
            }

            var remaining = timeout - stopwatch.Elapsed;
            var delay = remaining < TimeSpan.FromMilliseconds(100)
                ? remaining
                : TimeSpan.FromMilliseconds(100);

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private bool TryAcquireOnce()
    {
        try
        {
            return _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            return true;
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
