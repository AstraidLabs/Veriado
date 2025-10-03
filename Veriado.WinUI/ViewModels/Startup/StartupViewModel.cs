using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Veriado.WinUI.Errors;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.Views.Shell;
using Veriado.WinUI;

namespace Veriado.WinUI.ViewModels.Startup;

public partial class StartupViewModel : ObservableObject, IStartupReporter
{
    private readonly StartupDiagnosticsLog _diagnostics = new();
    private readonly Dictionary<AppStartupPhase, StartupStepViewModel> _stepsMap;

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    private bool hasError;

    [ObservableProperty]
    private string? statusMessage = "Připravuji…";

    [ObservableProperty]
    private string? detailsMessage;

    [ObservableProperty]
    private bool safeMode;

    [ObservableProperty]
    private string? diagnosticsLogPath;

    public StartupViewModel()
    {
        Steps = new ObservableCollection<StartupStepViewModel>(Enum
            .GetValues<AppStartupPhase>()
            .Select(phase => new StartupStepViewModel(phase, GetPhaseTitle(phase))));
        _stepsMap = Steps.ToDictionary(step => step.Phase);
    }

    public ObservableCollection<StartupStepViewModel> Steps { get; }

    public bool HasDiagnosticsLog => !string.IsNullOrWhiteSpace(DiagnosticsLogPath);

    public event EventHandler? RetryRequested;

    public void Report(AppStartupPhase phase, string message)
    {
        if (!_stepsMap.TryGetValue(phase, out var step))
        {
            return;
        }

        if (step.Status == StartupStepStatus.Pending)
        {
            step.Start(message);
            _diagnostics.RecordStart(phase, message);
        }
        else
        {
            step.Message = message;
            if (step.Status == StartupStepStatus.Running)
            {
                _diagnostics.RecordUpdate(phase, message);
            }
        }

        StatusMessage = message;
        DetailsMessage = null;
        HasError = false;
        IsLoading = true;
    }

    public async Task<bool> RunStartupAsync()
    {
        if (!AppHost.IsBuilt)
        {
            AppHost.Build();
        }

        IsLoading = true;
        HasError = false;
        DetailsMessage = null;
        SafeMode = false;
        DiagnosticsLogPath = null;

        foreach (var step in Steps)
        {
            step.Reset();
        }

        _diagnostics.Clear();

        var logger = AppHost.Services.GetService<ILogger<StartupViewModel>>();

        try
        {
            await RunStepAsync(
                AppStartupPhase.Bootstrap,
                "Inicializuji Windows App SDK…",
                () =>
                {
                    App.Current.InitializeWindowsAppSdkSafe(this);
                    return Task.CompletedTask;
                },
                logger).ConfigureAwait(true);

            await RunStepAsync(
                AppStartupPhase.StorageCheck,
                "Kontroluji databázi…",
                () => EnsureStorageExistsSafe(logger),
                logger).ConfigureAwait(true);

            await RunStepAsync(
                AppStartupPhase.HostBuild,
                "Připravuji služby…",
                async () =>
                {
                    await RunStepAsync(
                        AppStartupPhase.Migrations,
                        "Provádím migrace…",
                        AppHost.StartAsync,
                        logger).ConfigureAwait(true);
                },
                logger).ConfigureAwait(true);

            await RunStepAsync(
                AppStartupPhase.HotState,
                "Načítám uživatelské nastavení…",
                () => InitializeHotStateSafe(logger),
                logger).ConfigureAwait(true);

            await RunStepAsync(
                AppStartupPhase.Shell,
                "Spouštím rozhraní…",
                ShowMainShellAsync,
                logger).ConfigureAwait(true);

            IsLoading = false;
            StatusMessage = "Veriado je připraveno.";
            return true;
        }
        catch (InitializationException initEx)
        {
            logger?.LogError(initEx, "Startup initialization failed.");
            await AppHost.StopAsync().ConfigureAwait(true);
            await ShowErrorWithActionAsync(
                initEx.Message,
                initEx.Hint ?? "Zkuste to prosím znovu.",
                initEx).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Unexpected error during startup.");
            await AppHost.StopAsync().ConfigureAwait(true);
            await ShowErrorWithActionAsync(
                "Neočekávaná chyba při startu.",
                ex.Message,
                ex).ConfigureAwait(true);
        }

        return false;
    }

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private void Retry()
    {
        RetryRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CanRetry() => HasError;

    partial void OnHasErrorChanged(bool value)
    {
        RetryCommand.NotifyCanExecuteChanged();
    }

    private async Task EnsureStorageExistsSafe(ILogger? logger)
    {
        try
        {
            var provider = AppHost.Services.GetRequiredService<IInfrastructureConfigProvider>();
            await provider.EnsureStorageExistsSafe().ConfigureAwait(true);
        }
        catch (InitializationException)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InitializationException(
                "Nedostatečná oprávnění pro vytvoření databáze.",
                ex,
                "Spusťte aplikaci s oprávněními nebo vyberte jiný adresář.");
        }
        catch (IOException ex)
        {
            throw new InitializationException(
                "Chyba vstupu/výstupu při vytváření databáze.",
                ex,
                "Zkontrolujte volné místo a přístup k souboru/databázi.");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Unexpected error while ensuring storage availability.");
            throw;
        }
    }

    private async Task InitializeHotStateSafe(ILogger? logger)
    {
        try
        {
            var hotState = AppHost.Services.GetRequiredService<IHotStateService>();
            await hotState.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SafeMode = true;
            var warningMessage = "HotState selhal – spouštím bezpečný režim…";
            StatusMessage = warningMessage;
            logger?.LogWarning(ex, "HotState initialization failed. Starting in Safe Mode.");
            MarkPhaseWarning(AppStartupPhase.HotState, warningMessage, ex);
        }
    }

    private async Task ShowMainShellAsync()
    {
        var services = AppHost.Services;

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

        App.Current.RegisterMainWindow(shell);
    }

    private async Task ShowErrorWithActionAsync(string title, string? detail, Exception exception)
    {
        StatusMessage = title;
        DetailsMessage = detail;
        HasError = true;
        IsLoading = false;

        var logPath = await _diagnostics.FlushAsync(exception, SafeMode).ConfigureAwait(true);
        DiagnosticsLogPath = logPath;
    }

    private async Task RunStepAsync(
        AppStartupPhase phase,
        string message,
        Func<Task> action,
        ILogger? logger)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        Report(phase, message);

        if (!_stepsMap.TryGetValue(phase, out var step))
        {
            await action().ConfigureAwait(true);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await action().ConfigureAwait(true);
            stopwatch.Stop();

            var finalStatus = step.Status == StartupStepStatus.Running
                ? StartupStepStatus.Succeeded
                : step.Status;

            step.Complete(stopwatch.Elapsed, finalStatus);
            _diagnostics.RecordCompletion(phase, finalStatus, stopwatch.Elapsed, step.Message, step.ErrorMessage);
            logger?.LogInformation(
                "Startup phase {Phase} finished with {Status} in {Elapsed} ms",
                phase,
                finalStatus,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            step.Fail(ex, stopwatch.Elapsed);
            _diagnostics.RecordFailure(phase, ex, stopwatch.Elapsed);
            logger?.LogError(
                ex,
                "Startup phase {Phase} failed after {Elapsed} ms",
                phase,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private void MarkPhaseWarning(AppStartupPhase phase, string message, Exception exception)
    {
        if (!_stepsMap.TryGetValue(phase, out var step))
        {
            return;
        }

        step.Warn(message, exception);
        _diagnostics.RecordWarning(phase, message, exception);
    }

    partial void OnDiagnosticsLogPathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasDiagnosticsLog));
    }

    private static string GetPhaseTitle(AppStartupPhase phase) => phase switch
    {
        AppStartupPhase.Bootstrap => "Inicializace platformy",
        AppStartupPhase.StorageCheck => "Kontrola úložiště",
        AppStartupPhase.HostBuild => "Spuštění aplikačních služeb",
        AppStartupPhase.Migrations => "Databázové migrace",
        AppStartupPhase.HotState => "Načtení uživatelského nastavení",
        AppStartupPhase.Shell => "Inicializace uživatelského rozhraní",
        _ => phase.ToString()
    };
}
