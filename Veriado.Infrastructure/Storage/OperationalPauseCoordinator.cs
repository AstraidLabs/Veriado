using System;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Application.Abstractions;

namespace Veriado.Infrastructure.Storage;

public sealed class OperationalPauseCoordinator : IOperationalPauseCoordinator
{
    private TaskCompletionSource _resumeSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _isPaused;

    public bool IsPaused => _isPaused;

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
        _isPaused = true;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public void Resume()
    {
        _isPaused = false;
        _resumeSource.TrySetResult();
        _resumeSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        if (!_isPaused)
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return _resumeSource.Task.WaitAsync(cancellationToken);
    }
}
