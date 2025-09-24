using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Veriado.Services.Abstractions;
using Veriado.Views;
using Veriado.WinUI.ViewModels.Settings;
using Veriado.WinUI.ViewModels.Startup;

namespace Veriado;

public partial class App : Application
{
    private static readonly ILogger<App> BootstrapLogger = LoggerFactory
        .Create(builder => builder.AddProvider(new DebugLoggerProvider()))
        .CreateLogger<App>();

    private AppHost? _appHost;

    public App()
    {
        InitializeComponent();
    }

    public static new App Current => (App)Application.Current;

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
            host = await Task.Run(AppHost.StartAsync).ConfigureAwait(true);

            var services = host.Services;

            var mainWindow = services.GetRequiredService<MainWindow>();
            var windowProvider = services.GetRequiredService<IWindowProvider>();
            windowProvider.SetWindow(mainWindow);

            var dispatcherService = services.GetRequiredService<IDispatcherService>();
            dispatcherService.ResetDispatcher(mainWindow.DispatcherQueue);

            var keyboardShortcuts = services.GetRequiredService<IKeyboardShortcutsService>();
            keyboardShortcuts.RegisterDefaultShortcuts();

            var themeService = services.GetRequiredService<IThemeService>();
            await themeService.InitializeAsync().ConfigureAwait(true);

            var settingsViewModel = services.GetRequiredService<SettingsViewModel>();
            settingsViewModel.SelectedTheme = themeService.CurrentTheme;

            mainWindow.Activate();
            mainWindow.Closed += OnWindowClosed;

            _appHost = host;
            MainWindow = mainWindow;

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

        if (_appHost is not null)
        {
            await _appHost.DisposeAsync().ConfigureAwait(false);
            _appHost = null;
        }

        MainWindow = null;
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
