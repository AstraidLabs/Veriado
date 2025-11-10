using System.Diagnostics;
using System.Globalization;
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

        while (!await TryInitializeAsync(startupViewModel).ConfigureAwait(true))
        {
            await WaitForRetryAsync(startupViewModel).ConfigureAwait(true);
        }

        startupWindow.Close();
    }

    private static Task WaitForRetryAsync(StartupViewModel viewModel)
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? sender, EventArgs e)
        {
            viewModel.RetryRequested -= Handler;
            completion.TrySetResult(null);
        }

        viewModel.RetryRequested += Handler;

        return completion.Task;
    }

    private async Task<bool> TryInitializeAsync(StartupViewModel startupViewModel)
    {
        startupViewModel.ShowProgress("Spouštím služby aplikace...");

        AppHost? host = null;

        try
        {
            host = await AppHost.StartAsync().ConfigureAwait(true);

            _appHost = host;

            var services = host.Services;

            var shell = services.GetRequiredService<MainShell>();

            var windowProvider = services.GetRequiredService<IWindowProvider>();
            windowProvider.SetWindow(shell);

            var dispatcherService = services.GetRequiredService<IDispatcherService>();
            dispatcherService.ResetDispatcher(shell.DispatcherQueue);

            var themeService = services.GetRequiredService<IThemeService>();
            await themeService.InitializeAsync().ConfigureAwait(true);

            _shutdownOrchestrator = services.GetRequiredService<IShutdownOrchestrator>();

            shell.Activate();
            shell.Closed += OnWindowClosed;

            MainWindow = shell;

            _appWindow = shell.TryGetAppWindow();
            if (_appWindow is not null)
            {
                _isAppWindowShutdownInProgress = false;
                _appWindow.Closing += OnAppWindowClosing;
            }

            return true;
        }
        catch (Exception ex)
        {
            startupViewModel.ShowError(
                "Nepodařilo se spustit aplikaci.",
                ex.Message);

            LogStartupFailure(host, ex);

            if (host is not null)
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }

            _appHost = null;
            MainWindow = null;
            return false;
        }
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

        HandleAppWindowClosingAsync(sender, orchestrator);

        async void HandleAppWindowClosingAsync(AppWindow window, IShutdownOrchestrator shutdownOrchestrator)
        {
            try
            {
                var result = await shutdownOrchestrator
                    .RequestAppShutdownAsync(ShutdownReason.AppWindowClosing)
                    .ConfigureAwait(true);

                if (result.IsAllowed)
                {
                    _isAppWindowShutdownInProgress = true;
                    MainWindow?.Close();
                }
            }
            catch (Exception ex)
            {
                BootstrapLogger.LogError(ex, "Shutdown orchestrator failed during AppWindow closing. Allowing close.");
                _isAppWindowShutdownInProgress = true;
                MainWindow?.Close();
            }
        }
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

        var host = _appHost;
        _appHost = null;

        if (host is not null)
        {
            _ = DisposeHostAsync(host);
        }

        MainWindow = null;
    }

    private static async Task DisposeHostAsync(AppHost host)
    {
        try
        {
            await host.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            BootstrapLogger.LogError(ex, "Best-effort host disposal failed after window closed.");
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
