using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

namespace Veriado.WinUI.ViewModels;

/// <summary>
/// Provides a shared base implementation for view models with busy/error state handling.
/// </summary>
public abstract partial class ViewModelBase : ObservableRecipient
{
    private readonly IMessenger _messenger;
    private CancellationTokenSource? _runningOperation;

    protected ViewModelBase(IMessenger messenger)
    {
        _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));
        IsActive = true;
    }

    /// <summary>
    /// Gets the messenger used for intra-application communication.
    /// </summary>
    protected IMessenger Messenger => _messenger;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool hasError;

    [ObservableProperty]
    private bool isInfoBarOpen = true;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? lastError;

    /// <summary>
    /// Executes the supplied asynchronous delegate while managing busy and error state.
    /// </summary>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="pendingMessage">Optional message displayed while the operation runs.</param>
    /// <param name="successMessage">Optional message displayed when the operation succeeds.</param>
    protected async Task SafeExecuteAsync(
        Func<CancellationToken, Task> operation,
        string? pendingMessage = null,
        string? successMessage = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        TryCancelRunning();
        using var cts = CreateLinkedCts(cancellationToken);
        var token = cts.Token;

        try
        {
            IsBusy = true;
            HasError = false;
            LastError = null;

            if (!string.IsNullOrWhiteSpace(pendingMessage))
            {
                StatusMessage = pendingMessage;
                IsInfoBarOpen = true;
            }

            await operation(token).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(successMessage))
            {
                StatusMessage = successMessage;
                IsInfoBarOpen = true;
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operace byla zrušena.";
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            HasError = true;
            StatusMessage = "Operace selhala.";
            IsInfoBarOpen = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Creates a linked cancellation token source that is tracked by the view model.
    /// </summary>
    protected CancellationTokenSource CreateLinkedCts(CancellationToken cancellationToken = default)
    {
        TryCancelRunning();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runningOperation = cts;
        return cts;
    }

    /// <summary>
    /// Attempts to cancel the running operation, if any.
    /// </summary>
    /// <returns><see langword="true"/> when a cancellation request was issued; otherwise <see langword="false"/>.</returns>
    protected bool TryCancelRunning()
    {
        if (_runningOperation is null)
        {
            return false;
        }

        try
        {
            if (!_runningOperation.IsCancellationRequested)
            {
                _runningOperation.Cancel();
            }
        }
        finally
        {
            _runningOperation.Dispose();
            _runningOperation = null;
        }

        return true;
    }

    partial void OnLastErrorChanged(string? value)
    {
        HasError = !string.IsNullOrWhiteSpace(value);
        if (HasError)
        {
            IsInfoBarOpen = true;
        }
    }
}
