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

        _partitionCount = Math.Max(1, options.Workers);
        if (_state.PartitionCount != _partitionCount)
        {
            throw new InvalidOperationException(
                "Write pipeline state partition count does not match configured worker count.");
        }

        var capacities = AllocatePartitionCapacities(options.WriteQueueCapacity, _partitionCount);
        _partitions = new Channel<WriteRequest>[_partitionCount];
        for (var index = 0; index < _partitionCount; index++)
        {
            var channelOptions = new BoundedChannelOptions(capacities[index])
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            };
            _partitions[index] = Channel.CreateBounded<WriteRequest>(channelOptions);
        }
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

    private static int[] AllocatePartitionCapacities(int totalCapacity, int partitionCount)
    {
        if (partitionCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(partitionCount));
        }

        if (totalCapacity < partitionCount)
        {
            totalCapacity = partitionCount;
        }

        var baseCapacity = Math.Max(1, totalCapacity / partitionCount);
        var remainder = totalCapacity % partitionCount;
        var capacities = new int[partitionCount];
        for (var index = 0; index < partitionCount; index++)
        {
            capacities[index] = baseCapacity + (index < remainder ? 1 : 0);
        }

        return capacities;
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
            return GetPartitionFromGuid(partitionKey.Value);
        }

        var next = Interlocked.Increment(ref _roundRobinIndex);
        var positive = next & int.MaxValue;
        return positive % _partitionCount;
    }

    private int GetPartitionFromGuid(Guid partitionKey)
    {
        var hash = partitionKey.GetHashCode();
        if (hash == int.MinValue)
        {
            hash = int.MaxValue;
        }

        var positive = Math.Abs(hash);
        return positive % _partitionCount;
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
        if (partitionId < 0 || partitionId >= _partitionCount)
        {
            throw new ArgumentOutOfRangeException(nameof(partitionId));
        }
    }
}
