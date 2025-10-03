using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Veriado.WinUI.Errors;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.Views.Shell;
using Veriado.WinUI;

namespace Veriado.WinUI.ViewModels.Startup;

public partial class StartupViewModel : ObservableObject, IStartupReporter
{
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

    public event EventHandler? RetryRequested;

    public void Report(AppStartupPhase phase, string message)
    {
        StatusMessage = message;
        DetailsMessage = null;
        HasError = false;
        IsLoading = true;
    }

    public async Task<bool> RunStartupAsync(Func<IStartupReporter, Task> hostStart)
    {
        if (hostStart is null)
        {
            throw new ArgumentNullException(nameof(hostStart));
        }

        if (!AppHost.IsBuilt)
        {
            AppHost.Build();
        }

        IsLoading = true;
        HasError = false;
        DetailsMessage = null;
        SafeMode = false;

        var logger = AppHost.Services.GetService<ILogger<StartupViewModel>>();

        try
        {
            await MeasureAsync(logger, "Bootstrap", () =>
            {
                App.Current.InitializeWindowsAppSdkSafe(this);
                return Task.CompletedTask;
            }).ConfigureAwait(true);

            Report(AppStartupPhase.StorageCheck, "Kontroluji databázi…");
            await MeasureAsync(logger, "StorageCheck", () => EnsureStorageExistsSafe(logger)).ConfigureAwait(true);

            Report(AppStartupPhase.HostBuild, "Připravuji služby…");
            await MeasureAsync(logger, "HostStart+Migrations", () => hostStart(this)).ConfigureAwait(true);

            Report(AppStartupPhase.HotState, "Načítám uživatelské nastavení…");
            await MeasureAsync(logger, "HotState", () => InitializeHotStateSafe(logger)).ConfigureAwait(true);

            Report(AppStartupPhase.Shell, "Spouštím rozhraní…");
            await MeasureAsync(logger, "Shell", ShowMainShellAsync).ConfigureAwait(true);

            IsLoading = false;
            return true;
        }
        catch (InitializationException initEx)
        {
            logger?.LogError(initEx, "Startup initialization failed.");
            await AppHost.StopAsync().ConfigureAwait(true);
            ShowErrorWithAction(initEx.Message, initEx.Hint ?? "Zkuste to prosím znovu.");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Unexpected error during startup.");
            await AppHost.StopAsync().ConfigureAwait(true);
            ShowErrorWithAction("Neočekávaná chyba při startu.", ex.Message);
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

    private static async Task MeasureAsync(ILogger? logger, string name, Func<Task> run)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await run().ConfigureAwait(true);
            logger?.LogInformation("{Step} OK in {Elapsed} ms", name, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "{Step} FAILED in {Elapsed} ms", name, sw.ElapsedMilliseconds);
            throw;
        }
        finally
        {
            sw.Stop();
        }
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
            logger?.LogWarning(ex, "HotState initialization failed. Starting in Safe Mode.");
            StatusMessage = "HotState selhal – spouštím bezpečný režim…";
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

    private void ShowErrorWithAction(string title, string? detail)
    {
        StatusMessage = string.IsNullOrWhiteSpace(detail) ? title : $"{title} — {detail}";
        DetailsMessage = detail;
        HasError = true;
        IsLoading = false;
    }
}
