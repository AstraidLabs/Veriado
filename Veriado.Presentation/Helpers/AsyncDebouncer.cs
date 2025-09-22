using System;
using System.Threading;
using System.Threading.Tasks;

namespace Veriado.Presentation.Helpers;

/// <summary>
/// Provides a lightweight helper for debouncing asynchronous operations.
/// </summary>
internal sealed class AsyncDebouncer
{
    private readonly TimeSpan _delay;
    private CancellationTokenSource? _cts;

    public AsyncDebouncer(TimeSpan delay)
    {
        _delay = delay;
    }

    /// <summary>
    /// Schedules the supplied asynchronous callback to execute after the configured delay.
    /// Any in-flight operation is cancelled before the new callback is scheduled.
    /// </summary>
    /// <param name="callback">The asynchronous callback to execute.</param>
    public void Enqueue(Func<Task> callback)
    {
        if (callback is null)
        {
            throw new ArgumentNullException(nameof(callback));
        }

        _cts?.Cancel();
        var current = new CancellationTokenSource();
        _cts = current;

        _ = DebounceAsync(callback, current);
    }

    private async Task DebounceAsync(Func<Task> callback, CancellationTokenSource current)
    {
        try
        {
            await Task.Delay(_delay, current.Token);
            current.Token.ThrowIfCancellationRequested();
            await callback();
        }
        catch (OperationCanceledException)
        {
            // Swallow cancellation as it is part of the debouncing contract.
        }
    }
}
