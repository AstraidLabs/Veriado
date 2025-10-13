using System;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Threading;
using Veriado.Appl.Search.Abstractions;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides <see cref="ISearchWriteTelemetry"/> backed by <see cref="Meter"/> instruments.
/// </summary>
internal sealed class SearchWriteTelemetry : ISearchWriteTelemetry
{
    private static readonly Meter Meter = new("Veriado.Search.Write", "1.0.0");

    private readonly ObservableGauge<long> _queueDepth;
    private readonly Histogram<double> _queueWait;
    private readonly Histogram<double> _batchDuration;
    private readonly Counter<long> _sqliteRetry;
    private readonly ObservableGauge<int> _ftsAvailable;
    private readonly Counter<long> _ftsRepair;
    private readonly ConcurrentDictionary<int, int> _partitionDepth = new();
    private int _ftsAvailabilityValue = 0;

    public SearchWriteTelemetry()
    {
        _queueDepth = Meter.CreateObservableGauge("write_queue_depth", ObserveQueueDepth);
        _queueWait = Meter.CreateHistogram<double>("write_queue_wait_ms");
        _batchDuration = Meter.CreateHistogram<double>("batch_duration_ms");
        _sqliteRetry = Meter.CreateCounter<long>("sqlite_retry_count");
        _ftsAvailable = Meter.CreateObservableGauge("fts_available", ObserveFtsAvailability);
        _ftsRepair = Meter.CreateCounter<long>("fts_repair_triggered_total");
    }

    public void UpdateQueueDepth(int partitionId, int depth)
    {
        _partitionDepth[partitionId] = depth;
    }

    public void RecordQueueWait(TimeSpan elapsed)
    {
        _queueWait.Record(elapsed.TotalMilliseconds);
    }

    public void RecordBatchDuration(TimeSpan elapsed, int itemCount)
    {
        _batchDuration.Record(elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("items", itemCount));
    }

    public void RecordSqliteRetry(int count)
    {
        if (count <= 0)
        {
            return;
        }

        _sqliteRetry.Add(count);
    }

    public void UpdateFtsAvailability(bool isAvailable)
    {
        Interlocked.Exchange(ref _ftsAvailabilityValue, isAvailable ? 1 : 0);
    }

    public void RecordFtsRepairTriggered()
    {
        _ftsRepair.Add(1);
    }

    private IEnumerable<Measurement<long>> ObserveQueueDepth()
    {
        foreach (var (partitionId, depth) in _partitionDepth)
        {
            yield return new Measurement<long>(depth, new KeyValuePair<string, object?>("partition", partitionId));
        }
    }

    private IEnumerable<Measurement<int>> ObserveFtsAvailability()
    {
        yield return new Measurement<int>(Interlocked.CompareExchange(ref _ftsAvailabilityValue, 0, 0));
    }
}
