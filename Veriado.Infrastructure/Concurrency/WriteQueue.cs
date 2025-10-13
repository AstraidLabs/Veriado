using System;
using System.Threading;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Veriado.Infrastructure.Concurrency;

/// <summary>
/// Provides a bounded, partition-aware queue of write operations executed by background workers.
/// </summary>
internal sealed class WriteQueue : IWriteQueue
{
    private readonly WritePipelineState _state;
    private readonly IWritePipelineTelemetry _telemetry;
    private readonly IClock _clock;
    private readonly ILogger<WriteQueue> _logger;
    private readonly Channel<WriteRequest>[] _partitions;
    private readonly int _partitionCount;
    private readonly int _perPartitionCapacity;
    private int _roundRobinIndex;

    public WriteQueue(
        InfrastructureOptions options,
        WritePipelineState state,
        IWritePipelineTelemetry telemetry,
        IClock clock,
        ILogger<WriteQueue> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var workers = Math.Max(1, options.Workers);
        _partitionCount = workers;
        _perPartitionCapacity = Math.Max(1, options.WriteQueueCapacity / workers);

        if (_state.PartitionCount != _partitionCount)
        {
            throw new InvalidOperationException(
                "Write pipeline state partition count does not match configured worker count.");
        }

        _partitions = new Channel<WriteRequest>[_partitionCount];
        for (var index = 0; index < _partitionCount; index++)
        {
            var channelOptions = new BoundedChannelOptions(_perPartitionCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            };
            _partitions[index] = Channel.CreateBounded<WriteRequest>(channelOptions);
        }

        _logger.LogDebug(
            "Write queue configured with {WorkerCount} workers, {PartitionCount} partitions, {Capacity} capacity per partition.",
            workers,
            _partitionCount,
            _perPartitionCapacity);
    }

    public async Task<T> EnqueueAsync<T>(
        Func<AppDbContext, CancellationToken, Task<T>> work,
        IReadOnlyList<QueuedFileWrite>? trackedFiles,
        Guid? partitionKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = default(CancellationTokenRegistration);
        if (cancellationToken.CanBeCanceled)
        {
            registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        }

        var inferredKey = ResolvePartitionKey(partitionKey, trackedFiles);
        var request = new WriteRequest(
            async (context, ct) =>
            {
                var value = await work(context, ct).ConfigureAwait(false);
                return (object?)value;
            },
            completion,
            trackedFiles,
            cancellationToken,
            inferredKey);

        var partitionId = SelectPartition(inferredKey);
        if ((uint)partitionId >= (uint)_partitions.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(partitionId), partitionId, "Partition index out of range.");
        }
        try
        {
            request.MarkEnqueued(_clock.UtcNow);
            await _partitions[partitionId].Writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
            _state.RecordEnqueue(partitionId);
            _telemetry.RecordQueueDepth(_state.TotalQueueDepth);
            var result = await completion.Task.ConfigureAwait(false);
            return result is T typed ? typed : default!;
        }
        finally
        {
            registration.Dispose();
        }
    }

    public async ValueTask<WriteRequest?> DequeueAsync(int partitionId, CancellationToken cancellationToken)
    {
        ValidatePartition(partitionId);
        var reader = _partitions[partitionId].Reader;

        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.TryRead(out var request))
            {
                RecordDequeueMetrics(partitionId, request);
                return request;
            }
        }

        return null;
    }

    public bool TryDequeue(int partitionId, out WriteRequest? request)
    {
        ValidatePartition(partitionId);
        var reader = _partitions[partitionId].Reader;
        if (reader.TryRead(out request) && request is not null)
        {
            RecordDequeueMetrics(partitionId, request);
            return true;
        }

        request = null;
        return false;
    }

    public void Complete(Exception? error = null)
    {
        foreach (var partition in _partitions)
        {
            partition.Writer.TryComplete(error);
        }
    }

    private Guid? ResolvePartitionKey(Guid? requested, IReadOnlyList<QueuedFileWrite>? trackedFiles)
    {
        if (requested.HasValue)
        {
            return requested;
        }

        if (trackedFiles is not { Count: > 0 })
        {
            return null;
        }

        var first = trackedFiles[0].Entity.Id;
        for (var index = 1; index < trackedFiles.Count; index++)
        {
            if (trackedFiles[index].Entity.Id != first)
            {
                _logger.LogWarning(
                    "Write request tracked multiple file identifiers; falling back to first ({Primary}).",
                    first);
                break;
            }
        }

        return first;
    }

    private int SelectPartition(Guid? partitionKey)
    {
        if (partitionKey.HasValue)
        {
            return GetPartitionIndex(partitionKey.Value, _partitions.Length);
        }

        var next = (uint)Interlocked.Increment(ref _roundRobinIndex);
        return (int)(next % (uint)_partitions.Length);
    }

    private static int GetPartitionIndex(Guid key, int workers)
    {
        if (workers <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workers));
        }

        var hash = unchecked((uint)key.GetHashCode());
        return (int)(hash % (uint)workers);
    }

    private void RecordDequeueMetrics(int partitionId, WriteRequest request)
    {
        var latency = _clock.UtcNow - request.EnqueuedAt;
        if (latency < TimeSpan.Zero)
        {
            latency = TimeSpan.Zero;
        }

        _state.RecordDequeue(partitionId, latency);
        _telemetry.RecordQueueDepth(_state.TotalQueueDepth);
        if (latency > TimeSpan.Zero)
        {
            _telemetry.RecordQueueLatency(latency);
        }
    }

    private void ValidatePartition(int partitionId)
    {
        if ((uint)partitionId >= (uint)_partitionCount)
        {
            throw new ArgumentOutOfRangeException(nameof(partitionId));
        }
    }
}
