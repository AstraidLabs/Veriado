namespace Veriado.Infrastructure.Search;

using System;
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
    private readonly Histogram<double> _verifyDurationHistogram = Meter.CreateHistogram<double>("fts_verify_duration_ms");
    private readonly Counter<long> _verifyDriftCounter = Meter.CreateCounter<long>("fts_verify_drift_total");
    private readonly Histogram<long> _ftsRequestedOffsetHistogram = Meter.CreateHistogram<long>("search_fts_requested_offset");
    private readonly Histogram<long> _ftsPageSizeHistogram = Meter.CreateHistogram<long>("search_fts_page_size");
    private readonly Histogram<long> _ftsCandidateLimitHistogram = Meter.CreateHistogram<long>("search_fts_candidate_limit");
    private readonly Histogram<long> _ftsReturnedHistogram = Meter.CreateHistogram<long>("search_fts_returned_items");
    private readonly Histogram<long> _ftsReportedTotalHistogram = Meter.CreateHistogram<long>("search_fts_reported_total");
    private readonly Histogram<long> _ftsActualTotalHistogram = Meter.CreateHistogram<long>("search_fts_actual_total");
    private readonly Counter<long> _ftsHasMoreCounter = Meter.CreateCounter<long>("search_fts_has_more_total");
    private readonly Counter<long> _ftsTruncatedCounter = Meter.CreateCounter<long>("search_fts_truncated_total");
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

    public void RecordIndexVerificationDuration(TimeSpan elapsed)
        => _verifyDurationHistogram.Record(elapsed.TotalMilliseconds);

    public void RecordIndexDrift(int driftCount)
    {
        if (driftCount <= 0)
        {
            return;
        }

        _verifyDriftCounter.Add(driftCount);
    }

    public void RecordFtsPagingMetrics(
        int requestedOffset,
        int pageSize,
        int candidateLimit,
        int maxCandidateResults,
        int returnedCount,
        int reportedTotalCount,
        int actualTotalCount,
        bool hasMore,
        bool isTruncated)
    {
        _ftsRequestedOffsetHistogram.Record(Math.Max(requestedOffset, 0));
        _ftsPageSizeHistogram.Record(Math.Max(pageSize, 0));
        _ftsCandidateLimitHistogram.Record(Math.Max(candidateLimit, 0));
        _ftsReturnedHistogram.Record(Math.Max(returnedCount, 0));
        _ftsReportedTotalHistogram.Record(Math.Max(reportedTotalCount, 0));
        _ftsActualTotalHistogram.Record(Math.Max(actualTotalCount, 0));

        if (hasMore)
        {
            _ftsHasMoreCounter.Add(1);
        }

        if (isTruncated || actualTotalCount > Math.Max(maxCandidateResults, 0))
        {
            _ftsTruncatedCounter.Add(1);
        }
    }

    private IEnumerable<Measurement<long>> ObserveDocuments()
    {
        yield return new Measurement<long>(Interlocked.Read(ref _documentCount));
    }

    private IEnumerable<Measurement<long>> ObserveIndexSize()
    {
        yield return new Measurement<long>(Interlocked.Read(ref _indexSizeBytes));
    }
}
