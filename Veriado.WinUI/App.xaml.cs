using System.Diagnostics;
using Veriado.WinUI.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Veriado.WinUI.ViewModels.Startup;
using Veriado.WinUI.Views;
using Veriado.WinUI.Views.Shell;
using Veriado.WinUI.Services;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI;

public partial class App : Application
{
    private static readonly ILogger<App> BootstrapLogger = LoggerFactory
        .Create(builder => builder.AddProvider(new DebugLoggerProvider()))
        .CreateLogger<App>();

    private AppHost? _appHost;

    public App()
    {
        InitializeComponent();

#if DEBUG
        try
        {
            this.DebugSettings.XamlResourceReferenceFailed += (s, e) =>
                System.Diagnostics.Debug.WriteLine("XamlResourceReferenceFailed: " + e.Message);
            this.DebugSettings.BindingFailed += (s, e) =>
                System.Diagnostics.Debug.WriteLine("BindingFailed: " + e.Message);
        }
        catch { }
#endif
    }



    public static new App Current => (App)Application.Current;

    public static IServiceProvider Services =>
        Current._appHost?.Services
        ?? throw new InvalidOperationException("Application host has not been started.");

    public Window? MainWindow { get; private set; }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);

        await ApplySavedCultureAsync().ConfigureAwait(true);

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
        startupViewModel.ShowProgress(LocalizedStrings.Get("Startup.InitializingServices"));

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

            var localizationService = services.GetRequiredService<ILocalizationService>();
            await localizationService.InitializeAsync().ConfigureAwait(true);

            var themeService = services.GetRequiredService<IThemeService>();
            await themeService.InitializeAsync().ConfigureAwait(true);

            shell.Activate();
            shell.Closed += OnWindowClosed;

            MainWindow = shell;

            return true;
        }
        catch (Exception ex)
        {
            startupViewModel.ShowError(
                LocalizedStrings.Get("Startup.StartupFailed"),
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

    private void LogShutdownFailure(AppHost? host, Exception exception)
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
                // Ignore errors retrieving the logger during shutdown failure handling.
            }
        }

        (logger ?? BootstrapLogger).LogError(exception, "Application shutdown failed.");
    }

    private async void OnWindowClosed(object sender, WindowEventArgs e)
    {
        if (sender is Window window)
        {
            window.Closed -= OnWindowClosed;
        }

        var host = _appHost;
        try
        {
            if (host is not null)
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LogShutdownFailure(host, ex);
        }
        finally
        {
            _appHost = null;
            MainWindow = null;
        }
    }

    private static async Task ApplySavedCultureAsync()
    {
        try
        {
            var settingsService = new JsonSettingsService();
            var settings = await settingsService.GetAsync().ConfigureAwait(true);
            var culture = LocalizationConfiguration.NormalizeCulture(settings.Language);
            CultureHelper.ApplyCulture(culture);
        }
        catch (Exception ex)
        {
            BootstrapLogger.LogWarning(ex, "Failed to apply saved culture before host startup.");
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
