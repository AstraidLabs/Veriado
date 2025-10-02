namespace Veriado.WinUI.ViewModels.Base;

/// <summary>
/// Provides a base implementation for WinUI view models.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    private readonly IMessenger _messenger;
    private readonly IStatusService _statusService;
    private readonly IDispatcherService _dispatcher;
    private readonly IExceptionHandler _exceptionHandler;
    private readonly ILocalizationService _localizationService;
    private CancellationTokenSource? _cancellationSource;

    protected ViewModelBase(
        IMessenger messenger,
        IStatusService statusService,
        IDispatcherService dispatcher,
        IExceptionHandler exceptionHandler,
        ILocalizationService localizationService)
    {
        _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));
        _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _exceptionHandler = exceptionHandler ?? throw new ArgumentNullException(nameof(exceptionHandler));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
    }

    protected IMessenger Messenger => _messenger;

    protected IStatusService StatusService => _statusService;

    protected IDispatcherService Dispatcher => _dispatcher;

    protected ILocalizationService LocalizationService => _localizationService;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool hasError;

    /// <summary>
    /// Attempts to cancel the currently running operation, if any.
    /// </summary>
    public void TryCancelRunning()
    {
        if (_cancellationSource is { IsCancellationRequested: false })
        {
            _cancellationSource.Cancel();
        }
    }

    /// <summary>
    /// Executes the supplied asynchronous delegate safely while tracking busy and error states.
    /// </summary>
    /// <param name="action">The asynchronous work to execute.</param>
    /// <param name="busyMessage">Optional busy indicator message.</param>
    protected async Task SafeExecuteAsync(Func<CancellationToken, Task> action, string? busyMessage = null)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        if (IsBusy)
        {
            return;
        }

        using var cts = new CancellationTokenSource();
        _cancellationSource = cts;

        await _dispatcher.Enqueue(() =>
        {
            HasError = false;
            IsBusy = true;
        });

        if (!string.IsNullOrWhiteSpace(busyMessage))
        {
            _statusService.Info(busyMessage);
        }

        try
        {
            await action(cts.Token);
            await _dispatcher.Enqueue(() => HasError = false);
        }
        catch (OperationCanceledException)
        {
            await _dispatcher.Enqueue(() => HasError = false);
            _statusService.Info(GetString("Common.OperationCancelled"));
        }
        catch (Exception ex)
        {
            var message = _exceptionHandler.Handle(ex);
            await _dispatcher.Enqueue(() => HasError = true);
            if (!string.IsNullOrWhiteSpace(message))
            {
                _statusService.Error(message);
            }
        }
        finally
        {
            _cancellationSource = null;
            await _dispatcher.Enqueue(() => IsBusy = false);

            if (!HasError && !string.IsNullOrWhiteSpace(busyMessage))
            {
                _statusService.Clear();
            }
        }
    }

    protected string GetString(string resourceKey, string? defaultValue = null, params object?[] arguments)
        => _localizationService.GetString(resourceKey, defaultValue, arguments);
}
