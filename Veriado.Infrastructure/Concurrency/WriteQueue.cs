using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Veriado.Infrastructure.Persistence;

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
        await _channel.Writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        var result = await completion.Task.ConfigureAwait(false);
        return result is T typed ? typed : default!;
    }

    public async ValueTask<WriteRequest?> DequeueAsync(CancellationToken cancellationToken)
    {
        while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_channel.Reader.TryRead(out var request))
            {
                return request;
            }
        }

        return null;
    }
}
