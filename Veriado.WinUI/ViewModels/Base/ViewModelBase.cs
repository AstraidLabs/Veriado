using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Veriado.ViewModels.Base;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool hasError;

    [ObservableProperty]
    private string? statusMessage;

    protected CancellationTokenSource? Cts { get; private set; }

    protected async Task SafeExecuteAsync(Func<CancellationToken, Task> action, string? busyMessage = null)
    {
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
        Cts = cts;
        IsBusy = true;

        try
        {
            await action(cts.Token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(busyMessage) && StatusMessage == busyMessage)
            {
                StatusMessage = null;
            }
        }
        catch (OperationCanceledException)
        {
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
            Cts = null;
        }
    }

    public void Cancel() => Cts?.Cancel();
}
