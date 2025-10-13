using System;
using System.Threading;

namespace Veriado.Infrastructure.Concurrency;

/// <summary>
/// Tracks shared statistics for the queued write pipeline.
/// </summary>
internal sealed class WritePipelineState
{
    private readonly int[] _partitionDepths;
    private readonly int _partitionCount;

    private long _totalDepth;
    private long _queueLatencyTicks;
    private long _queueLatencySamples;
    private long _lastBatchCompletedTicks;
    private long _lastBatchDurationTicks;
    private long _lastBatchRetryCount;
    private long _lastBatchSize;
    private int _lastBatchPartitionId;

    public WritePipelineState(int partitionCount)
    {
        if (partitionCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(partitionCount));
        }

        _partitionCount = partitionCount;
        _partitionDepths = new int[partitionCount];
        var now = DateTimeOffset.UtcNow.UtcTicks;
        Volatile.Write(ref _lastBatchCompletedTicks, now);
    }

    public int PartitionCount => _partitionCount;

    public int TotalQueueDepth => (int)Volatile.Read(ref _totalDepth);

    public double AverageQueueLatencyMs
    {
        get
        {
            var samples = Volatile.Read(ref _queueLatencySamples);
            if (samples <= 0)
            {
                return 0d;
            }

            var totalTicks = Volatile.Read(ref _queueLatencyTicks);
            var averageTicks = totalTicks / Math.Max(1, samples);
            return TimeSpan.FromTicks(averageTicks).TotalMilliseconds;
        }
    }

    public DateTimeOffset LastBatchCompletedUtc
    {
        get
        {
            var ticks = Volatile.Read(ref _lastBatchCompletedTicks);
            return new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public TimeSpan LastBatchDuration => TimeSpan.FromTicks(Volatile.Read(ref _lastBatchDurationTicks));

    public int LastBatchRetryCount => (int)Volatile.Read(ref _lastBatchRetryCount);

    public int LastBatchSize => (int)Volatile.Read(ref _lastBatchSize);

    public int LastBatchPartitionId => Volatile.Read(ref _lastBatchPartitionId);

    public void RecordEnqueue(int partitionId)
    {
        ValidatePartition(partitionId);
        Interlocked.Increment(ref _partitionDepths[partitionId]);
        Interlocked.Increment(ref _totalDepth);
    }

    public void RecordDequeue(int partitionId, TimeSpan queueLatency)
    {
        ValidatePartition(partitionId);
        Interlocked.Decrement(ref _partitionDepths[partitionId]);
        Interlocked.Decrement(ref _totalDepth);

        if (queueLatency <= TimeSpan.Zero)
        {
            return;
        }

        Interlocked.Add(ref _queueLatencyTicks, queueLatency.Ticks);
        Interlocked.Increment(ref _queueLatencySamples);
    }

    public void RecordBatch(int partitionId, int itemCount, TimeSpan duration, int retryCount)
    {
        ValidatePartition(partitionId);
        Interlocked.Exchange(ref _lastBatchPartitionId, partitionId);
        Interlocked.Exchange(ref _lastBatchDurationTicks, duration.Ticks);
        Interlocked.Exchange(ref _lastBatchRetryCount, retryCount);
        Interlocked.Exchange(ref _lastBatchSize, itemCount);
        var completedTicks = DateTimeOffset.UtcNow.UtcTicks;
        Interlocked.Exchange(ref _lastBatchCompletedTicks, completedTicks);
    }

    private void ValidatePartition(int partitionId)
    {
        if (partitionId < 0 || partitionId >= _partitionCount)
        {
            throw new ArgumentOutOfRangeException(nameof(partitionId));
        }
    }
}
