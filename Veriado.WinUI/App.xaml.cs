using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.ApplicationModel.DynamicDependency;
using Veriado.WinUI.Errors;
using Veriado.WinUI.Helpers;
using Veriado.WinUI.ViewModels.Startup;
using Veriado.WinUI.Views;
using Windows.ApplicationModel;

namespace Veriado.WinUI;

public partial class App : Application
{
    private static readonly ILogger<App> BootstrapLogger = LoggerFactory
        .Create(builder => builder.AddProvider(new DebugLoggerProvider()))
        .CreateLogger<App>();

    private static readonly SemaphoreSlim ShutdownSemaphore = new(1, 1);

    private static bool _isBootstrapInitialized;
    private static bool _isShutdownStarted;

    public App()
    {
        InitializeComponent();
        AppHost.Build();

        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    public static new App Current => (App)Application.Current;

    public static IServiceProvider Services => AppHost.Services;

    public Window? MainWindow { get; private set; }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);

        ApplyDefaultCulture();
        AnimationSettings.ConfigureDispatcherQueue(DispatcherQueue.GetForCurrentThread());

        var startupWindow = new StartupWindow();
        var startupViewModel = new StartupViewModel();

        if (startupWindow.Content is FrameworkElement rootElement)
        {
            rootElement.DataContext = startupViewModel;
        }
        startupWindow.Activate();

        async Task<bool> RunAsync() => await startupViewModel.RunStartupAsync().ConfigureAwait(true);

        while (!await RunAsync().ConfigureAwait(true))
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

    private async void OnWindowClosed(object sender, WindowEventArgs e)
    {
        if (sender is Window window)
        {
            window.Closed -= OnWindowClosed;
        }

        await StopHostSafelyAsync().ConfigureAwait(false);

        MainWindow = null;

        ShutdownWindowsAppSdk();
    }

    private static void OnProcessExit(object? sender, EventArgs e)
    {
        StopHostSafelyBlocking();
    }

    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        StopHostSafelyBlocking();
    }

    private static void ApplyDefaultCulture()
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch (CultureNotFoundException ex)
        {
            BootstrapLogger.LogWarning(ex, "Failed to apply default culture.");
        }
    }

    private static bool IsRunningInPackagedContext()
    {
        try
        {
            _ = Package.Current;
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }

        try
        {
            return Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().IsCurrent;
        }
        catch
        {
            return false;
        }
    }

    internal void InitializeWindowsAppSdkSafe(IStartupReporter reporter)
    {
        try
        {
            if (IsRunningInPackagedContext())
            {
                reporter.Report(AppStartupPhase.Bootstrap, "Windows App SDK je připravena.");
                BootstrapLogger.LogInformation(
                    "Skipping Windows App SDK bootstrap initialization because the runtime is provided by the app package.");
                return;
            }

            if (_isBootstrapInitialized)
            {
                reporter.Report(AppStartupPhase.Bootstrap, "Windows App SDK je připravena.");
                return;
            }

            reporter.Report(AppStartupPhase.Bootstrap, "Inicializuji Windows App SDK…");
            const uint majorMinorVersion = 0x00010008; // Windows App SDK 1.8
            Bootstrap.Initialize(majorMinorVersion);
            _isBootstrapInitialized = true;
        }
        catch (Exception ex)
        {
            BootstrapLogger.LogError(ex, "Failed to initialize Windows App SDK bootstrapper.");
            throw new InitializationException(
                "Windows App SDK bootstrap selhal. Zkontrolujte instalaci Windows App SDK runtime.",
                ex,
                "Nainstalujte nebo opravte Windows App SDK Runtime a spusťte aplikaci znovu.");
        }
    }

    private static void ShutdownWindowsAppSdk()
    {
        if (!_isBootstrapInitialized)
        {
            return;
        }

        try
        {
            Bootstrap.Shutdown();
        }
        finally
        {
            _isBootstrapInitialized = false;
        }
    }

    private static async Task StopHostSafelyAsync()
    {
        if (!AppHost.IsBuilt)
        {
            return;
        }

        var shouldStop = false;

        await ShutdownSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_isShutdownStarted)
            {
                _isShutdownStarted = true;
                shouldStop = true;
            }
        }
        finally
        {
            ShutdownSemaphore.Release();
        }

        if (!shouldStop)
        {
            return;
        }

        try
        {
            await AppHost.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            BootstrapLogger.LogError(ex, "Failed to stop AppHost during shutdown.");
        }
    }

    private static void StopHostSafelyBlocking()
    {
        try
        {
            StopHostSafelyAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            BootstrapLogger.LogError(ex, "Failed to synchronously stop AppHost during shutdown.");
        }
        finally
        {
            ShutdownWindowsAppSdk();
        }
    }

    internal void RegisterMainWindow(Window window)
    {
        if (window is null)
        {
            throw new ArgumentNullException(nameof(window));
        }

        if (MainWindow is not null)
        {
            MainWindow.Closed -= OnWindowClosed;
        }

        MainWindow = window;
        window.Closed += OnWindowClosed;
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
