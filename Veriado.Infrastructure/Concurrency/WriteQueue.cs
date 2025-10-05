using System.Threading.Channels;

namespace Veriado.Infrastructure.Concurrency;

/// <summary>
/// Provides a bounded queue of write operations executed by the background worker.
/// </summary>
internal sealed class WriteQueue : IWriteQueue
{
    private readonly Channel<WriteRequest> _channel;

    public WriteQueue(int capacity = 10_000)
    {
        var options = new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        };
        _channel = Channel.CreateBounded<WriteRequest>(options);
    }

    public async Task<T> EnqueueAsync<T>(
        Func<AppDbContext, CancellationToken, Task<T>> work,
        IReadOnlyList<QueuedFileWrite>? trackedFiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new WriteRequest(async (context, ct) =>
        {
            var value = await work(context, ct).ConfigureAwait(false);
            return (object?)value;
        }, completion, trackedFiles, cancellationToken);
        using var registration = cancellationToken.Register(() => request.TrySetCanceled(cancellationToken));
        await _channel.Writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        var result = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return result is T typed ? typed : default!;
    }

    public async ValueTask<WriteRequest?> DequeueAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        while (await WaitToReadWithoutThrowingAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_channel.Reader.TryRead(out var request))
            {
                return request;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }

        return null;
    }

    private async ValueTask<bool> WaitToReadWithoutThrowingAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        var waitToRead = _channel.Reader.WaitToReadAsync();
        if (waitToRead.IsCompletedSuccessfully)
        {
            return waitToRead.Result;
        }

        if (!cancellationToken.CanBeCanceled)
        {
            return await waitToRead.ConfigureAwait(false);
        }

        var waitTask = waitToRead.AsTask();
        if (waitTask.IsCompleted)
        {
            return await waitTask.ConfigureAwait(false);
        }

        var cancellationSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(static state =>
        {
            var tcs = (TaskCompletionSource<bool>)state!;
            tcs.TrySetResult(false);
        }, cancellationSource);

        var completed = await Task.WhenAny(waitTask, cancellationSource.Task).ConfigureAwait(false);
        if (completed == waitTask)
        {
            return await waitTask.ConfigureAwait(false);
        }

        return false;
    }

    public bool TryDequeue(out WriteRequest? request) => _channel.Reader.TryRead(out request);

    public void Complete(Exception? error = null) => _channel.Writer.TryComplete(error);
}
