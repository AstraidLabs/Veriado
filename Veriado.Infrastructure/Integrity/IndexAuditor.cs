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
    private readonly InfrastructureOptions _options;
    private readonly ISearchTelemetry _telemetry;
    private readonly ILogger<IndexAuditor> _logger;

    public IndexAuditor(
        INeedsReindexEvaluator evaluator,
        IDbContextFactory<ReadOnlyDbContext> readFactory,
        IIndexQueue indexQueue,
        InfrastructureOptions options,
        ISearchTelemetry telemetry,
        ILogger<IndexAuditor> logger)
    {
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _readFactory = readFactory ?? throw new ArgumentNullException(nameof(readFactory));
        _indexQueue = indexQueue ?? throw new ArgumentNullException(nameof(indexQueue));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AuditSummary> VerifyAsync(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Starting search index audit");
        var missing = new HashSet<Guid>();
        var drift = new HashSet<Guid>();
        var extra = new HashSet<Guid>();

        var (indexAvailable, searchMap) = await LoadIndexSnapshotAsync(ct).ConfigureAwait(false);

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
            var isMissing = false;
            if (indexAvailable && !searchMap.Remove(fileId))
            {
                missing.Add(fileId);
                isMissing = true;
            }

            if (isMissing)
            {
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

        if (indexAvailable)
        {
            extra.UnionWith(searchMap);
        }

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

        _logger.LogInformation(
            "Index audit completed in {ElapsedMs} ms (missing={MissingCount}, drift={DriftCount}, extra={ExtraCount})",
            stopwatch.Elapsed.TotalMilliseconds,
            summary.Missing.Count,
            summary.Drift.Count,
            summary.Extra.Count);

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

        _logger.LogInformation(
            "Scheduled re-index of {Count} files based on audit summary (missing={Missing}, drift={Drift})",
            unique.Count,
            missing.Count,
            drift.Count);

        return Task.FromResult(unique.Count);
    }

    private async Task<(bool IndexAvailable, HashSet<Guid> SearchMap)> LoadIndexSnapshotAsync(CancellationToken ct)
    {
        if (!_options.IsFulltextAvailable || string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            return (false, new HashSet<Guid>());
        }

        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken: ct).ConfigureAwait(false);

            var searchMap = await LoadDocumentIdsAsync(connection, ct).ConfigureAwait(false);
            return (true, searchMap);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is SqliteException || ex is InvalidOperationException)
        {
            _logger.LogWarning(ex, "Full-text index metadata could not be read during verification.");
            return (false, new HashSet<Guid>());
        }
    }

    private static async Task<HashSet<Guid>> LoadDocumentIdsAsync(SqliteConnection connection, CancellationToken ct)
    {
        var ids = new HashSet<Guid>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT file_id FROM DocumentContent;";
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0))
            {
                continue;
            }

            if (reader.GetFieldType(0) == typeof(byte[]))
            {
                var blob = (byte[])reader[0];
                if (blob.Length == 16)
                {
                    ids.Add(new Guid(blob));
                }
                continue;
            }

            if (reader[0] is Guid guid)
            {
                ids.Add(guid);
            }
            else if (Guid.TryParse(reader[0]?.ToString(), out var parsed))
            {
                ids.Add(parsed);
            }
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
