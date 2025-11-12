using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Veriado.WinUI.Helpers;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.Services.Shutdown;
using Veriado.WinUI.ViewModels.Startup;
using Veriado.WinUI.Views;
using Veriado.WinUI.Views.Shell;

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private bool _shutdownCompleted;
    private bool _forceQuitRequested;

    public App()
    {
        InitializeComponent();

        UnhandledException += OnAppUnhandledException;
        TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

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

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);

        ObserveTask(HandleLaunchAsync(args), "application launch");
    }

    private async Task HandleLaunchAsync(LaunchActivatedEventArgs args)
    {
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

            _shutdownCompleted = false;
            _forceQuitRequested = false;

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

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isAppWindowShutdownInProgress || _forceQuitRequested)
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
        var deferral = args.GetDeferral();
        ObserveTask(HandleCloseAsync(deferral), "window closing");
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

        if (!_shutdownCompleted && !_forceQuitRequested)
        {
            GetLogger().LogWarning("App window closed without coordinated shutdown; clearing host references defensively.");
        }

        _isAppWindowShutdownInProgress = false;
        _appHost = null;
        _shutdownOrchestrator = null;

        MainWindow = null;
    }

    private async Task HandleCloseAsync(AppWindowClosingDeferral? deferral)
    {
        var forceExit = false;

        try
        {
            var host = _appHost;
            if (host is null)
            {
                _shutdownCompleted = true;
                _forceQuitRequested = true;
                MainWindow?.Close();
                return;
            }

            var services = host.Services;
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

            while (true)
            {
                _isAppWindowShutdownInProgress = true;

                var orchestrator = _shutdownOrchestrator;
                if (orchestrator is null)
                {
                    _shutdownCompleted = true;
                    MainWindow?.Close();
                    return;
                }

                var shutdownResult = await orchestrator
                    .RequestAppShutdownAsync(ShutdownReason.AppWindowClosing)
                    .ConfigureAwait(true);

                LogShutdownResult(shutdownResult);

                switch (shutdownResult.Status)
                {
                    case ShutdownStatus.Success:
                        _shutdownCompleted = true;
                        MainWindow?.Close();
                        return;

                    case ShutdownStatus.Canceled:
                        _isAppWindowShutdownInProgress = false;
                        return;

                    case ShutdownStatus.Failed:
                        var choice = await ShowShutdownFailureDialogAsync(shutdownResult).ConfigureAwait(true);
                        if (choice == ShutdownFailureChoice.Retry)
                        {
                            _isAppWindowShutdownInProgress = false;
                            continue;
                        }

                        if (choice == ShutdownFailureChoice.Force)
                        {
                            forceExit = PrepareForceQuit(shutdownResult);
                            return;
                        }

                        _isAppWindowShutdownInProgress = false;
                        return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _isAppWindowShutdownInProgress = false;
        }
        catch (Exception ex)
        {
            GetLogger().LogError(ex, "Shutdown orchestrator failed during AppWindow closing. Allowing close.");
            _isAppWindowShutdownInProgress = true;
            _shutdownCompleted = true;
            MainWindow?.Close();
        }
        finally
        {
            deferral?.Complete();

            if (forceExit)
            {
                Environment.ExitCode = 1;
                Environment.Exit(1);
            }
        }
    }

    private enum ShutdownFailureChoice
    {
        Retry,
        Force,
        Cancel,
    }

    private void ObserveTask(Task task, string operation)
    {
        if (task is null)
        {
            return;
        }

        task.ContinueWith(t =>
        {
            if (!t.IsFaulted || t.Exception is null)
            {
                return;
            }

            var logger = GetLogger();
            var exception = t.Exception.Flatten().InnerExceptions.Count == 1
                ? t.Exception.InnerException ?? t.Exception
                : t.Exception;
            logger.LogError(exception, "Unhandled exception during {Operation}.", operation);
        }, TaskScheduler.Default);
    }

    private ILogger<App> GetLogger()
    {
        var host = _appHost;
        if (host is not null)
        {
            try
            {
                return host.Services.GetService<ILogger<App>>() ?? BootstrapLogger;
            }
            catch
            {
                return BootstrapLogger;
            }
        }

        return BootstrapLogger;
    }

    private void LogShutdownResult(ShutdownResult result)
    {
        var logger = GetLogger();

        switch (result.Status)
        {
            case ShutdownStatus.Success:
                logger.LogInformation(
                    "Coordinated shutdown completed in {Duration}. Host stop={HostStop}, dispose={HostDispose}.",
                    result.Duration,
                    result.Host.Stop.State,
                    result.Host.Dispose.State);
                break;
            case ShutdownStatus.Canceled:
                logger.LogInformation("Shutdown canceled after {Duration}.", result.Duration);
                break;
            case ShutdownStatus.Failed:
                var failure = result.Failure;
                logger.LogWarning(
                    "Shutdown failed after {Duration}. Phase={Phase}, Reason={Reason}, HostStop={HostStop}, HostDispose={HostDispose}.",
                    result.Duration,
                    failure?.Phase ?? ShutdownFailurePhase.None,
                    failure?.Reason ?? ShutdownFailureReason.Unknown,
                    result.Host.Stop.State,
                    result.Host.Dispose.State);

                if (failure?.Exception is not null)
                {
                    logger.LogDebug(failure.Exception, "Shutdown failure details.");
                }

                break;
        }
    }

    private async Task<ShutdownFailureChoice> ShowShutdownFailureDialogAsync(ShutdownResult result)
    {
        if (MainWindow?.Content is not FrameworkElement root || root.XamlRoot is null)
        {
            GetLogger().LogWarning("Unable to display shutdown failure dialog because XamlRoot is unavailable.");
            return ShutdownFailureChoice.Cancel;
        }

        var failure = result.Failure;
        var phaseText = failure?.Phase switch
        {
            ShutdownFailurePhase.LifecycleStop => "Zastavení životního cyklu",
            ShutdownFailurePhase.HostStop => "Zastavení hostitele",
            ShutdownFailurePhase.HostDispose => "Uvolnění hostitele",
            _ => "Neznámá fáze",
        };

        var reasonText = failure?.Reason switch
        {
            ShutdownFailureReason.Timeout => "Operace vypršela.",
            ShutdownFailureReason.Canceled => "Operace byla zrušena.",
            ShutdownFailureReason.Exception => failure.Exception?.Message ?? "Došlo k výjimce.",
            ShutdownFailureReason.NotSupported => "Fáze ukončení není podporována.",
            ShutdownFailureReason.Unknown or ShutdownFailureReason.None => "Došlo k neočekávané chybě.",
            _ => "Došlo k neočekávané chybě.",
        };

        var message =
            $"Ukončení aplikace se nezdařilo.\n\n" +
            $"Fáze: {phaseText}\n" +
            $"Důvod: {reasonText}\n" +
            $"Host stop: {result.Host.Stop.State}\n" +
            $"Host dispose: {result.Host.Dispose.State}\n" +
            $"Doba pokusu: {result.Duration:g}\n\n" +
            "Zvolte další postup.";

        var dialog = new ContentDialog
        {
            Title = "Nelze ukončit aplikaci",
            Content = message,
            PrimaryButtonText = "Zkusit znovu",
            SecondaryButtonText = "Vynutit ukončení",
            CloseButtonText = "Zrušit",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root.XamlRoot,
            RequestedTheme = root.ActualTheme,
        };

        var dialogResult = await dialog.ShowAsync().AsTask().ConfigureAwait(true);
        return dialogResult switch
        {
            ContentDialogResult.Primary => ShutdownFailureChoice.Retry,
            ContentDialogResult.Secondary => ShutdownFailureChoice.Force,
            _ => ShutdownFailureChoice.Cancel,
        };
    }

    private bool PrepareForceQuit(ShutdownResult result)
    {
        var failure = result.Failure;
        var logger = GetLogger();
        logger.LogCritical(
            "Force quitting application after shutdown failure. Phase={Phase}, Reason={Reason}, HostStop={HostStop}, HostDispose={HostDispose}.",
            failure?.Phase ?? ShutdownFailurePhase.None,
            failure?.Reason ?? ShutdownFailureReason.Unknown,
            result.Host.Stop.State,
            result.Host.Dispose.State);
        if (failure?.Exception is not null)
        {
            logger.LogCritical(failure.Exception, "Exception that prevented graceful shutdown.");
        }

        _forceQuitRequested = true;
        _shutdownCompleted = true;
        return true;
    }

    private void OnAppUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        if (e.Exception is not null)
        {
            GetLogger().LogError(e.Exception, "Unhandled WinUI exception.");
        }
        else
        {
            GetLogger().LogError("Unhandled WinUI exception without details.");
        }

        e.Handled = true;
    }

    private void OnTaskSchedulerUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        GetLogger().LogError(e.Exception, "Unobserved task exception encountered.");
        e.SetObserved();
    }

    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        var logger = GetLogger();
        if (e.ExceptionObject is Exception exception)
        {
            logger.LogCritical(exception, "AppDomain unhandled exception. Terminating={Terminating}.", e.IsTerminating);
        }
        else
        {
            logger.LogCritical(
                "AppDomain unhandled exception object: {ExceptionObject}. Terminating={Terminating}.",
                e.ExceptionObject,
                e.IsTerminating);
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
