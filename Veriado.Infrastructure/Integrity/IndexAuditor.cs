using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Search;

namespace Veriado.Infrastructure.Integrity;

public sealed class IndexAuditor : IIndexAuditor
{
    private readonly INeedsReindexEvaluator _evaluator;
    private readonly IDbContextFactory<ReadOnlyDbContext> _readFactory;
    private readonly IIndexQueue _indexQueue;
    private readonly ISearchTelemetry _telemetry;
    private readonly ILogger<IndexAuditor> _logger;
    private readonly LuceneIndexManager _luceneIndex;

    public IndexAuditor(
        INeedsReindexEvaluator evaluator,
        IDbContextFactory<ReadOnlyDbContext> readFactory,
        IIndexQueue indexQueue,
        LuceneIndexManager luceneIndex,
        ISearchTelemetry telemetry,
        ILogger<IndexAuditor> logger)
    {
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _readFactory = readFactory ?? throw new ArgumentNullException(nameof(readFactory));
        _indexQueue = indexQueue ?? throw new ArgumentNullException(nameof(indexQueue));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _luceneIndex = luceneIndex ?? throw new ArgumentNullException(nameof(luceneIndex));
    }

    public async Task<AuditSummary> VerifyAsync(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var missing = new HashSet<Guid>();
        var drift = new HashSet<Guid>();
        var extra = new HashSet<Guid>();

        var indexIds = await LoadIndexIdsAsync(ct).ConfigureAwait(false);

        await using var context = await _readFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var files = context.Files
            .AsNoTracking()
            .Include(f => f.Content)
            .AsAsyncEnumerable()
            .WithCancellation(ct);

        await foreach (var file in files.ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();

            var fileId = file.Id;
            if (!indexIds.Remove(fileId))
            {
                missing.Add(fileId);
                continue;
            }

            var state = file.SearchIndex ?? new SearchIndexState(schemaVersion: 1);
            var requiresReindex = state.IsStale;
            if (!requiresReindex)
            {
                requiresReindex = await _evaluator.NeedsReindexAsync(file, state, ct).ConfigureAwait(false);
            }

            if (requiresReindex)
            {
                drift.Add(fileId);
            }
        }

        extra.UnionWith(indexIds);

        var summary = new AuditSummary(
            ConvertToStrings(missing),
            ConvertToStrings(drift),
            ConvertToStrings(extra));

        stopwatch.Stop();
        _telemetry.RecordIndexVerificationDuration(stopwatch.Elapsed);
        var driftTotal = summary.Missing.Count + summary.Drift.Count;
        if (driftTotal > 0)
        {
            _telemetry.RecordIndexDrift(driftTotal);
        }

        return summary;
    }

    public Task<int> RepairDriftAsync(AuditSummary summary, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ct.ThrowIfCancellationRequested();

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = summary.Missing ?? new List<string>();
        var drift = summary.Drift ?? new List<string>();

        foreach (var id in missing.Concat(drift))
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (!unique.Add(id))
            {
                continue;
            }

            _indexQueue.Enqueue(new IndexDocument(id));
        }

        return Task.FromResult(unique.Count);
    }

    private async Task<HashSet<Guid>> LoadIndexIdsAsync(CancellationToken ct)
    {
        var ids = new HashSet<Guid>();
        try
        {
            await _luceneIndex.EnsureInitializedAsync(ct).ConfigureAwait(false);
            using var reader = _luceneIndex.OpenReader();
            for (var i = 0; i < reader.NumDocs; i++)
            {
                ct.ThrowIfCancellationRequested();
                var document = reader.Document(i);
                if (Guid.TryParse(document.Get(LuceneIndexManager.FieldNames.Id), out var parsed))
                {
                    ids.Add(parsed);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lucene index could not be read during verification.");
        }

        return ids;
    }

    private static List<string> ConvertToStrings(HashSet<Guid> source)
    {
        if (source.Count == 0)
        {
            return new List<string>();
        }

        var list = source
            .Select(id => id.ToString("D"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        list.Sort(StringComparer.Ordinal);
        return list;
    }
}
