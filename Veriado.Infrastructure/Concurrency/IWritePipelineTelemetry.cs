using System;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics.Metrics;

namespace Veriado.Infrastructure.Concurrency;

internal interface IWritePipelineTelemetry
{
    void RecordQueueDepth(int depth);

    void RecordQueueLatency(TimeSpan latency);

    void RecordBatchProcessed(int partitionId, int itemCount, TimeSpan duration, int retryCount);

    void RecordBatchFailure(int partitionId);

}

internal sealed class WritePipelineTelemetry : IWritePipelineTelemetry
{
    private static readonly Meter Meter = new("Veriado.WritePipeline", "1.0.0");

    private readonly Histogram<double> _queueLatencyHistogram = Meter.CreateHistogram<double>("write_queue_latency_ms");
    private readonly Histogram<double> _batchDurationHistogram = Meter.CreateHistogram<double>("write_batch_duration_ms");
    private readonly Histogram<long> _batchSizeHistogram = Meter.CreateHistogram<long>("write_batch_size");
    private readonly Counter<long> _batchRetryCounter = Meter.CreateCounter<long>("write_batch_retries_total");
    private readonly Counter<long> _batchFailureCounter = Meter.CreateCounter<long>("write_batch_failures_total");
    private readonly ObservableGauge<long> _queueDepthGauge;
    private readonly ObservableGauge<double> _queueLatencyGauge;
    private readonly WritePipelineState _state;

    private long _queueDepth;

    public WritePipelineTelemetry(WritePipelineState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _queueDepthGauge = Meter.CreateObservableGauge("write_queue_depth", ObserveQueueDepth);
        _queueLatencyGauge = Meter.CreateObservableGauge("write_queue_latency_avg_ms", ObserveQueueLatency);
    }

    public void RecordQueueDepth(int depth)
    {
        Interlocked.Exchange(ref _queueDepth, depth);
    }

    public void RecordQueueLatency(TimeSpan latency)
    {
        if (latency <= TimeSpan.Zero)
        {
            return;
        }

        _queueLatencyHistogram.Record(latency.TotalMilliseconds);
    }

    public void RecordBatchProcessed(int partitionId, int itemCount, TimeSpan duration, int retryCount)
    {
        _batchDurationHistogram.Record(
            duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("partition", partitionId));
        _batchSizeHistogram.Record(
            itemCount,
            new KeyValuePair<string, object?>("partition", partitionId));

        if (retryCount > 0)
        {
            _batchRetryCounter.Add(
                retryCount,
                new KeyValuePair<string, object?>("partition", partitionId));
        }
    }

    public void RecordBatchFailure(int partitionId)
    {
        _batchFailureCounter.Add(1, new KeyValuePair<string, object?>("partition", partitionId));
    }

    private IEnumerable<Measurement<long>> ObserveQueueDepth()
    {
        yield return new Measurement<long>(Interlocked.Read(ref _queueDepth));
    }

    private IEnumerable<Measurement<double>> ObserveQueueLatency()
    {
        yield return new Measurement<double>(_state.AverageQueueLatencyMs);
    }
}
