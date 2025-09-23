using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.ViewModels.Base;

/// <summary>
/// Provides a base implementation for WinUI view models.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    private readonly IMessenger _messenger;
    private readonly IStatusService _statusService;
    private CancellationTokenSource? _cancellationSource;

    protected ViewModelBase(IMessenger messenger, IStatusService statusService)
    {
        _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));
        _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
    }

    /// <summary>
    /// Gets the messenger instance used to communicate between view models.
    /// </summary>
    protected IMessenger Messenger => _messenger;

    /// <summary>
    /// Gets the status service used to broadcast status updates.
    /// </summary>
    protected IStatusService StatusService => _statusService;

    /// <summary>
    /// Gets a value indicating whether status changes should be broadcast through the messenger.
    /// </summary>
    protected virtual bool BroadcastStatusChanges => true;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool hasError;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private bool isInfoBarOpen;

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

        HasError = false;
        if (!string.IsNullOrWhiteSpace(busyMessage))
        {
            StatusMessage = busyMessage;
        }

        using var cts = new CancellationTokenSource();
        _cancellationSource = cts;
        IsBusy = true;

        try
        {
            await action(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            HasError = false;
            StatusMessage = "Operace byla zrušena.";
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            _cancellationSource = null;

            if (!HasError && !string.IsNullOrWhiteSpace(busyMessage) && StatusMessage == busyMessage)
            {
                StatusMessage = null;
            }
        }
    }

    partial void OnStatusMessageChanged(string? value)
    {
        IsInfoBarOpen = !string.IsNullOrWhiteSpace(value);

        if (BroadcastStatusChanges)
        {
            PublishStatus();
        }
    }

    partial void OnHasErrorChanged(bool value)
    {
        if (BroadcastStatusChanges)
        {
            PublishStatus();
        }
    }

    private void PublishStatus()
    {
        if (string.IsNullOrWhiteSpace(StatusMessage))
        {
            _statusService.Clear();
            return;
        }

        if (HasError)
        {
            _statusService.ShowError(StatusMessage);
        }
        else
        {
            _statusService.ShowInfo(StatusMessage);
        }
    }
}
