namespace Veriado.Infrastructure.Search;

using System.Diagnostics.Metrics;
using System.Threading;

/// <summary>
/// Provides <see cref="ISearchTelemetry"/> implementation backed by <see cref="Meter"/> instruments.
/// </summary>
internal sealed class SearchTelemetry : ISearchTelemetry
{
    private static readonly Meter Meter = new("Veriado.Search", "1.0.0");

    private readonly Histogram<double> _ftsQueryHistogram = Meter.CreateHistogram<double>("search_fts_query_ms");
    private readonly Histogram<double> _trigramQueryHistogram = Meter.CreateHistogram<double>("search_trigram_query_ms");
    private readonly Histogram<double> _facetHistogram = Meter.CreateHistogram<double>("search_facet_ms");
    private readonly Histogram<double> _overallHistogram = Meter.CreateHistogram<double>("search_latency_ms");
    private readonly Histogram<double> _verifyDurationHistogram = Meter.CreateHistogram<double>("fts_verify_duration_ms");
    private readonly Counter<long> _indexDriftCounter = Meter.CreateCounter<long>("index_drift_count");
    private readonly Counter<long> _repairBatchCounter = Meter.CreateCounter<long>("repair_batches_total");
    private readonly Counter<long> _repairFailureCounter = Meter.CreateCounter<long>("repair_failures_total");
    private readonly Counter<long> _sqliteBusyRetryCounter = Meter.CreateCounter<long>("sqlite_busy_retries_total");
    private readonly ObservableGauge<long> _documentGauge;
    private readonly ObservableGauge<long> _indexSizeGauge;
    private readonly ObservableGauge<long> _deadLetterGauge;
    private long _documentCount;
    private long _indexSizeBytes;
    private long _deadLetterCount;

    public SearchTelemetry()
    {
        _documentGauge = Meter.CreateObservableGauge("search_documents_total", ObserveDocuments);
        _indexSizeGauge = Meter.CreateObservableGauge("search_index_bytes", ObserveIndexSize);
        _deadLetterGauge = Meter.CreateObservableGauge("fts_dlq_size", ObserveDeadLetterSize);
    }

    public void RecordFtsQuery(TimeSpan elapsed)
        => _ftsQueryHistogram.Record(elapsed.TotalMilliseconds);

    public void RecordTrigramQuery(TimeSpan elapsed)
        => _trigramQueryHistogram.Record(elapsed.TotalMilliseconds);

    public void RecordFacetComputation(TimeSpan elapsed)
        => _facetHistogram.Record(elapsed.TotalMilliseconds);

    public void RecordSearchLatency(TimeSpan elapsed)
        => _overallHistogram.Record(elapsed.TotalMilliseconds);

    public void UpdateIndexMetrics(long documentCount, long indexSizeBytes)
    {
        Interlocked.Exchange(ref _documentCount, documentCount);
        Interlocked.Exchange(ref _indexSizeBytes, indexSizeBytes);
    }

    public void UpdateDeadLetterQueueSize(long entryCount)
        => Interlocked.Exchange(ref _deadLetterCount, entryCount);

    public void RecordIndexVerificationDuration(TimeSpan elapsed)
        => _verifyDurationHistogram.Record(elapsed.TotalMilliseconds);

    public void RecordIndexDrift(int driftCount)
    {
        if (driftCount <= 0)
        {
            return;
        }

        _indexDriftCounter.Add(driftCount);
    }

    public void RecordRepairBatch(int batchSize)
    {
        if (batchSize <= 0)
        {
            return;
        }

        _repairBatchCounter.Add(1, new KeyValuePair<string, object?>("documents", batchSize));
    }

    public void RecordRepairFailure()
        => _repairFailureCounter.Add(1);

    public void RecordSqliteBusyRetry(int retryCount)
    {
        if (retryCount <= 0)
        {
            return;
        }

        _sqliteBusyRetryCounter.Add(retryCount);
    }

    private IEnumerable<Measurement<long>> ObserveDocuments()
    {
        yield return new Measurement<long>(Interlocked.Read(ref _documentCount));
    }

    private IEnumerable<Measurement<long>> ObserveIndexSize()
    {
        yield return new Measurement<long>(Interlocked.Read(ref _indexSizeBytes));
    }

    private IEnumerable<Measurement<long>> ObserveDeadLetterSize()
    {
        yield return new Measurement<long>(Interlocked.Read(ref _deadLetterCount));
    }
}
