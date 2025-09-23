using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.WinUI.ViewModels.Messages;

namespace Veriado.WinUI.ViewModels.Base;

/// <summary>
/// Provides a base implementation for WinUI view models.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    private readonly IMessenger _messenger;
    private CancellationTokenSource? _cancellationSource;

    protected ViewModelBase(IMessenger messenger)
    {
        _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));
    }

    /// <summary>
    /// Gets the messenger instance used to communicate between view models.
    /// </summary>
    protected IMessenger Messenger => _messenger;

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
            _messenger.Send(new StatusChangedMessage(value, HasError));
        }
    }

    partial void OnHasErrorChanged(bool value)
    {
        if (BroadcastStatusChanges)
        {
            _messenger.Send(new StatusChangedMessage(StatusMessage, value));
        }
    }
}
