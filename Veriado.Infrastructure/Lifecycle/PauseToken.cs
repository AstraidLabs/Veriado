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

    public Task WaitIfPausedAsync(CancellationToken cancellationToken = default) =>
        _source?.WaitIfPausedAsync(cancellationToken) ?? Task.CompletedTask;

    public Task WhenPausedAsync(CancellationToken cancellationToken = default) =>
        _source?.WhenPausedAsync(cancellationToken) ?? Task.CompletedTask;

    public Task WhenResumedAsync(CancellationToken cancellationToken = default) =>
        _source?.WhenResumedAsync(cancellationToken) ?? Task.CompletedTask;
}

public sealed class PauseTokenSource
{
    private readonly object _sync = new();
    private TaskCompletionSource<bool>? _pauseCompletion;
    private TaskCompletionSource<bool> _pauseSignal = CreateSignal();
    private TaskCompletionSource<bool> _resumeSignal = CreateSignal(completed: true);

    public bool IsPaused { get; private set; }

    public PauseToken Token => new(this);

    public bool TryPause()
    {
        TaskCompletionSource<bool> pauseSignal;
        lock (_sync)
        {
            if (IsPaused)
            {
                return false;
            }

            pauseSignal = _pauseSignal;
            _pauseSignal = CreateSignal();
            _resumeSignal = CreateSignal();
            _pauseCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            IsPaused = true;
        }

        pauseSignal.TrySetResult(true);
        return true;
    }

    public bool TryResume()
    {
        TaskCompletionSource<bool>? completion;
        TaskCompletionSource<bool> resumeSignal;
        lock (_sync)
        {
            if (!IsPaused)
            {
                return false;
            }

            IsPaused = false;
            completion = _pauseCompletion;
            _pauseCompletion = null;
            resumeSignal = _resumeSignal;
            _resumeSignal = CreateSignal(completed: true);
            _pauseSignal = CreateSignal();
        }

        completion?.TrySetResult(true);
        resumeSignal.TrySetResult(true);
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

    internal Task WhenPausedAsync(CancellationToken cancellationToken)
    {
        Task task;
        lock (_sync)
        {
            if (IsPaused)
            {
                return Task.CompletedTask;
            }

            task = _pauseSignal.Task;
        }

        return WaitInternalAsync(task, cancellationToken);
    }

    internal Task WhenResumedAsync(CancellationToken cancellationToken)
    {
        Task task;
        lock (_sync)
        {
            if (!IsPaused)
            {
                return Task.CompletedTask;
            }

            task = _resumeSignal.Task;
        }

        return WaitInternalAsync(task, cancellationToken);
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

    private static TaskCompletionSource<bool> CreateSignal(bool completed = false)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (completed)
        {
            tcs.TrySetResult(true);
        }

        return tcs;
    }
}
