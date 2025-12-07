using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using Microsoft.UI.Dispatching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Forms = System.Windows.Forms;
using Veriado.WinUI.ViewModels.Startup;
using Veriado.WinUI.Views;
using Veriado.WinUI.Views.Shell;

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
    private Forms.NotifyIcon? _notifyIcon;
    private bool _isShuttingDown;
    private bool _notificationsRegistered;

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

    internal bool IsShuttingDown => _isShuttingDown;

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);

        EnsureAppNotificationsRegistered();

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

            shell.Activate();
            shell.Closed += OnWindowClosed;

            MainWindow = shell;

            InitializeTrayIcon();

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

    private async void OnWindowClosed(object sender, WindowEventArgs e)
    {
        if (sender is Window window)
        {
            window.Closed -= OnWindowClosed;
        }

        await DisposeHostAsync().ConfigureAwait(false);
        DisposeTrayIcon();

        MainWindow = null;
    }

    internal void HideMainWindow()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (MainWindow)
            {
                case MainShell shell:
                    shell.HideShell();
                    break;
                case not null:
                    MainWindow.Hide();
                    break;
            }
        });
    }

    private void ShowMainWindow()
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            var window = await EnsureMainWindowAsync().ConfigureAwait(false);

            switch (window)
            {
                case MainShell shell:
                    shell.ShowShell();
                    break;
                case not null:
                    window.Activate();
                    break;
            }
        });
    }

    private async Task<Window?> EnsureMainWindowAsync()
    {
        if (MainWindow is not null)
        {
            return MainWindow;
        }

        if (_appHost is null)
        {
            return null;
        }

        var services = _appHost.Services;
        var shell = services.GetRequiredService<MainShell>();

        var windowProvider = services.GetRequiredService<IWindowProvider>();
        windowProvider.SetWindow(shell);

        var dispatcherService = services.GetRequiredService<IDispatcherService>();
        dispatcherService.ResetDispatcher(shell.DispatcherQueue);

        shell.Closed += OnWindowClosed;
        shell.Activate();

        MainWindow = shell;

        return shell;
    }

    private void InitializeTrayIcon()
    {
        if (_notifyIcon is not null)
        {
            return;
        }

        var contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Items.Add("Otevřít Veriado", null, (_, _) => ShowMainWindow());
        contextMenu.Items.Add("Restartovat", null, (_, _) => RestartApplication());
        contextMenu.Items.Add("Ukončit", null, (_, _) => ExitApplication());

        var icon = LoadTrayIcon() ?? SystemIcons.Application;

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = icon,
            Visible = true,
            Text = "Veriado – správce dokumentů",
            ContextMenuStrip = contextMenu,
        };

        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private static Icon? LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "favicon.ico");

        if (File.Exists(iconPath))
        {
            try
            {
                return new Icon(iconPath);
            }
            catch
            {
                // Ignore icon loading failures; a fallback icon will be used.
            }
        }

        return null;
    }

    private async void ExitApplication()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;

        DisposeTrayIcon();
        await DisposeHostAsync().ConfigureAwait(false);

        WinUIApplication.Current.Exit();
    }

    private async void RestartApplication()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;

        DisposeTrayIcon();
        await DisposeHostAsync().ConfigureAwait(false);

        var restartResult = AppInstance.Restart(string.Empty);
        if (restartResult != AppRestartFailureReason.Ok)
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = processPath,
                        UseShellExecute = true,
                        WorkingDirectory = AppContext.BaseDirectory,
                    });
                }
                catch (Exception ex)
                {
                    BootstrapLogger.LogError(ex, "Failed to restart application");
                }
            }
        }

        WinUIApplication.Current.Exit();
    }

    private async Task DisposeHostAsync()
    {
        if (_appHost is null)
        {
            return;
        }

        await _appHost.DisposeAsync().ConfigureAwait(false);
        _appHost = null;
    }

    private void DisposeTrayIcon()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _notifyIcon = null;
    }

    private void EnsureAppNotificationsRegistered()
    {
        if (_notificationsRegistered)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.Register();
            _notificationsRegistered = true;
        }
        catch (Exception ex)
        {
            BootstrapLogger.LogError(ex, "Failed to register app notifications");
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
