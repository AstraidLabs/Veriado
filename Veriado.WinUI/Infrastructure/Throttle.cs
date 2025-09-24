using System;
using System.Threading;
using System.Threading.Tasks;

namespace Veriado.WinUI.Infrastructure;

public sealed class Throttle : IDisposable
{
    private readonly TimeSpan _delay;
    private CancellationTokenSource? _cts;

    public Throttle(TimeSpan delay)
    {
        _delay = delay;
    }

    public async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            await Task.Delay(_delay, token).ConfigureAwait(false);
            await action(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
