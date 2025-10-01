namespace Veriado.Infrastructure.Search;

using System.Diagnostics.Metrics;

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
    private readonly ObservableGauge<long> _documentGauge;
    private readonly ObservableGauge<long> _indexSizeGauge;
    private readonly Histogram<int> _outboxAttemptsHistogram = Meter.CreateHistogram<int>("outbox_attempts_histogram");
    private readonly Counter<long> _outboxDlqCounter = Meter.CreateCounter<long>("outbox_dlq_total");

    private long _documentCount;
    private long _indexSizeBytes;

    public SearchTelemetry()
    {
        _documentGauge = Meter.CreateObservableGauge("search_documents_total", ObserveDocuments);
        _indexSizeGauge = Meter.CreateObservableGauge("search_index_bytes", ObserveIndexSize);
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

    public void RecordOutboxAttempt(int attempts)
        => _outboxAttemptsHistogram.Record(attempts);

    public void RecordOutboxDeadLetter()
        => _outboxDlqCounter.Add(1);

    private IEnumerable<Measurement<long>> ObserveDocuments()
    {
        yield return new Measurement<long>(Interlocked.Read(ref _documentCount));
    }

    private IEnumerable<Measurement<long>> ObserveIndexSize()
    {
        yield return new Measurement<long>(Interlocked.Read(ref _indexSizeBytes));
    }
}
