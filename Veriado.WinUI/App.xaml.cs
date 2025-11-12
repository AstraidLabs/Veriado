using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Veriado.WinUI.Helpers;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.ViewModels.Startup;
using Veriado.WinUI.Views;
using Veriado.WinUI.Views.Shell;

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinUIApplication = Microsoft.UI.Xaml.Application;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace Veriado.WinUI;

public partial class App : WinUIApplication
{
    private static readonly ILogger<App> BootstrapLogger = LoggerFactory
        .Create(builder => builder.AddProvider(new DebugLoggerProvider()))
        .CreateLogger<App>();

    private AppHost? _appHost;
    private AppWindow? _appWindow;
    private IShutdownOrchestrator? _shutdownOrchestrator;
    private bool _isAppWindowShutdownInProgress;

    public App()
    {
        InitializeComponent();

        var czechCulture = CultureInfo.GetCultureInfo("cs-CZ");
        CultureInfo.DefaultThreadCurrentCulture = czechCulture;
        CultureInfo.DefaultThreadCurrentUICulture = czechCulture;
        CultureInfo.CurrentCulture = czechCulture;
        CultureInfo.CurrentUICulture = czechCulture;
    }

    public static new App Current => (App)WinUIApplication.Current;

    public static IServiceProvider Services =>
        Current._appHost?.Services
        ?? throw new InvalidOperationException("Application host has not been started.");

    public Window? MainWindow { get; private set; }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);

        var startupViewModel = new StartupViewModel();
        var startupWindow = new StartupWindow(startupViewModel);
        startupWindow.Activate();

        using var startupCts = new CancellationTokenSource();

        void OnStartupWindowClosed(object? sender, WindowEventArgs e)
        {
            startupWindow.Closed -= OnStartupWindowClosed;
            startupCts.Cancel();
        }

        startupWindow.Closed += OnStartupWindowClosed;

        try
        {
            while (!startupCts.IsCancellationRequested)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(startupCts.Token);

                if (await TryInitializeAsync(startupViewModel, linkedCts.Token).ConfigureAwait(true))
                {
                    startupWindow.Close();
                    return;
                }

                try
                {
                    await WaitForRetryAsync(startupViewModel, startupCts.Token).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            startupWindow.Closed -= OnStartupWindowClosed;
        }
    }

    private static async Task WaitForRetryAsync(StartupViewModel viewModel, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? sender, EventArgs e)
        {
            viewModel.RetryRequested -= Handler;
            completion.TrySetResult(null);
        }

        viewModel.RetryRequested += Handler;

        using var registration = cancellationToken.Register(() =>
        {
            viewModel.RetryRequested -= Handler;
            completion.TrySetCanceled(cancellationToken);
        });

        await completion.Task.ConfigureAwait(true);
    }

    private async Task<bool> TryInitializeAsync(StartupViewModel startupViewModel, CancellationToken cancellationToken)
    {
        startupViewModel.ShowProgress("Spouštím služby aplikace...");

        AppHost? host = null;
        var initialized = false;

        try
        {
            host = await AppHost.StartAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            _appHost = host;

            var services = host.Services;

            _shutdownOrchestrator = services.GetRequiredService<IShutdownOrchestrator>();

            var startupCoordinator = services.GetRequiredService<IStartupCoordinator>();
            var result = await startupCoordinator.RunAsync(cancellationToken).ConfigureAwait(true);

            var shell = result.Shell;
            shell.Activate();
            shell.Closed += OnWindowClosed;

            MainWindow = shell;

            _appWindow = shell.TryGetAppWindow();
            if (_appWindow is not null)
            {
                _isAppWindowShutdownInProgress = false;
                _appWindow.Closing += OnAppWindowClosing;
            }

            initialized = true;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation requested by caller; ensure cleanup before returning.
        }
        catch (Exception ex)
        {
            startupViewModel.ShowError(
                "Nepodařilo se spustit aplikaci.",
                ex.Message);

            LogStartupFailure(host, ex);
        }

        if (!initialized)
        {
            if (host is not null)
            {
                try
                {
                    await host.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception disposeEx)
                {
                    BootstrapLogger.LogError(disposeEx, "Best-effort host disposal failed after startup error.");
                }
            }

            _appHost = null;
            _shutdownOrchestrator = null;
            MainWindow = null;
            _appWindow = null;
        }

        return false;
    }

    private void LogStartupFailure(AppHost? host, Exception exception)
    {
        ILogger<App>? logger = null;

        if (host is not null)
        {
            try
            {
                logger = host.Services.GetService<ILogger<App>>();
            }
            catch
            {
                // Ignore errors retrieving the logger during startup failure handling.
            }
        }

        (logger ?? BootstrapLogger).LogError(exception, "Application startup failed.");
    }

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isAppWindowShutdownInProgress)
        {
            args.Cancel = false;
            return;
        }

        var orchestrator = _shutdownOrchestrator;
        if (orchestrator is null)
        {
            args.Cancel = false;
            return;
        }

        args.Cancel = true;

        await HandleCloseAsync().ConfigureAwait(true);
    }

    private void OnWindowClosed(object sender, WindowEventArgs e)
    {
        if (sender is Window window)
        {
            window.Closed -= OnWindowClosed;
        }

        if (_appWindow is not null)
        {
            _appWindow.Closing -= OnAppWindowClosing;
            _appWindow = null;
        }

        _appHost = null;
        _shutdownOrchestrator = null;

        MainWindow = null;
    }

    private async Task HandleCloseAsync()
    {
        try
        {
            var services = Services;
            var confirmService = services.GetRequiredService<IConfirmService>();

            var options = new ConfirmOptions
            {
                Timeout = Timeout.InfiniteTimeSpan,
                CancellationToken = CancellationToken.None,
            };

            var confirmed = await confirmService
                .TryConfirmAsync("Ukončit aplikaci?", "Opravdu si přejete ukončit aplikaci?", "Ukončit", "Zůstat", options)
                .ConfigureAwait(true);

            if (!confirmed)
            {
                return;
            }

            _isAppWindowShutdownInProgress = true;

            var orchestrator = _shutdownOrchestrator;
            if (orchestrator is null)
            {
                MainWindow?.Close();
                return;
            }

            var result = await orchestrator
                .RequestAppShutdownAsync(ShutdownReason.AppWindowClosing)
                .ConfigureAwait(true);

            if (result.IsAllowed)
            {
                MainWindow?.Close();
            }
            else
            {
                _isAppWindowShutdownInProgress = false;
            }
        }
        catch (OperationCanceledException)
        {
            _isAppWindowShutdownInProgress = false;
        }
        catch (Exception ex)
        {
            BootstrapLogger.LogError(ex, "Shutdown orchestrator failed during AppWindow closing. Allowing close.");
            _isAppWindowShutdownInProgress = true;
            MainWindow?.Close();
        }
    }

    private sealed class DebugLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new DebugLogger(categoryName);

        public void Dispose()
        {
        }

        private sealed class DebugLogger : ILogger
        {
            private readonly string _categoryName;

            public DebugLogger(string categoryName)
            {
                _categoryName = categoryName;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (formatter is null)
                {
                    throw new ArgumentNullException(nameof(formatter));
                }

                var message = formatter(state, exception);
                if (string.IsNullOrWhiteSpace(message) && exception is null)
                {
                    return;
                }

                Debug.WriteLine($"{DateTimeOffset.Now:u} [{logLevel}] {_categoryName}: {message}");
                if (exception is not null)
                {
                    Debug.WriteLine(exception);
                }
            }
        }
    }
}
