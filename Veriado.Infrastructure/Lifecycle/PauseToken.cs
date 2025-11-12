using System;
using System.Threading;
using System.Threading.Tasks;

namespace Veriado.Infrastructure.Lifecycle;

public readonly struct PauseToken
{
    private readonly PauseTokenSource? _source;

    internal PauseToken(PauseTokenSource? source)
    {
        _source = source;
    }

    public bool IsPaused => _source?.IsPaused ?? false;

    public Task WaitIfPausedAsync(CancellationToken cancellationToken = default)
    {
        return _source?.WaitIfPausedAsync(cancellationToken) ?? Task.CompletedTask;
    }
}

public sealed class PauseTokenSource
{
    private readonly object _sync = new();
    private TaskCompletionSource<bool>? _pauseCompletion;

    public bool IsPaused { get; private set; }

    public PauseToken Token => new(this);

    public bool TryPause()
    {
        lock (_sync)
        {
            if (IsPaused)
            {
                return false;
            }

            _pauseCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            IsPaused = true;
            return true;
        }
    }

    public bool TryResume()
    {
        TaskCompletionSource<bool>? completion;
        lock (_sync)
        {
            if (!IsPaused)
            {
                return false;
            }

            IsPaused = false;
            completion = _pauseCompletion;
            _pauseCompletion = null;
        }

        completion?.TrySetResult(true);
        return true;
    }

    internal Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool>? completion;
        lock (_sync)
        {
            if (!IsPaused)
            {
                return Task.CompletedTask;
            }

            completion = _pauseCompletion ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return WaitInternalAsync(completion.Task, cancellationToken);
    }

    private static async Task WaitInternalAsync(Task pauseTask, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            await pauseTask.ConfigureAwait(false);
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(static state =>
        {
            var completion = (TaskCompletionSource<object?>)state!;
            completion.TrySetResult(null);
        }, tcs);

        var completed = await Task.WhenAny(pauseTask, tcs.Task).ConfigureAwait(false);
        if (completed == pauseTask)
        {
            await pauseTask.ConfigureAwait(false);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
