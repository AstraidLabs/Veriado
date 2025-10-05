using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Documents;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;
using Veriado.Appl.Search;
using Veriado.Domain.Search;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Executes Lucene.NET based queries over the hybrid search index.
/// </summary>
internal sealed class LuceneQueryService
{
    private const string SourceName = "LUCENE";
    private const int DefaultSnippetLength = 240;

    private readonly LuceneIndexManager _indexManager;
    private readonly ISearchTelemetry _telemetry;
    private readonly ILogger<LuceneQueryService> _logger;
    public LuceneQueryService(
        LuceneIndexManager indexManager,
        ISearchTelemetry telemetry,
        ILogger<LuceneQueryService> logger)
    {
        _indexManager = indexManager ?? throw new ArgumentNullException(nameof(indexManager));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<(Guid Id, double Score)>> SearchWithScoresAsync(
        SearchQueryPlan plan,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (take <= 0)
        {
            return Array.Empty<(Guid, double)>();
        }

        var hits = await ExecuteSearchAsync(plan, skip, take, includeDocuments: false, cancellationToken)
            .ConfigureAwait(false);
        return hits.Select(hit => (hit.Id, hit.Score)).ToList();
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        SearchQueryPlan plan,
        int take,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (take <= 0)
        {
            return Array.Empty<SearchHit>();
        }

        var hits = await ExecuteSearchAsync(plan, skip: 0, take, includeDocuments: true, cancellationToken)
            .ConfigureAwait(false);

        return hits.Select(hit => hit.ToSearchHit()).ToList();
    }

    private async Task<List<LuceneHit>> ExecuteSearchAsync(
        SearchQueryPlan plan,
        int skip,
        int take,
        bool includeDocuments,
        CancellationToken cancellationToken)
    {
        if (take <= 0)
        {
            return new List<LuceneHit>();
        }

        if (skip < 0)
        {
            skip = 0;
        }

        await _indexManager.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        BooleanQuery.SetMaxClauseCount(Math.Max(BooleanQuery.MaxClauseCount, 1024));

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var reader = _indexManager.OpenReader();
            var searcher = new IndexSearcher(reader);
            var query = BuildQuery(plan);
            if (query is null)
            {
                return new List<LuceneHit>();
            }

            var limit = checked(skip + take);
            var docs = searcher.Search(query, limit);
            var scored = docs.ScoreDocs.Skip(skip).ToArray();
            if (scored.Length == 0)
            {
                return new List<LuceneHit>();
            }

            var maxScore = docs.MaxScore <= 0 ? 1f : docs.MaxScore;
            var results = new List<LuceneHit>(scored.Length);
            foreach (var scoreDoc in scored)
            {
                var doc = includeDocuments ? searcher.Doc(scoreDoc.Doc) : null;
                if (doc is null && includeDocuments)
                {
                    continue;
                }

                Guid id;
                if (doc is not null)
                {
                    if (!Guid.TryParse(doc.Get(LuceneIndexManager.FieldNames.Id), out id))
                    {
                        continue;
                    }
                }
                else
                {
                    id = ExtractId(searcher, scoreDoc.Doc);
                    if (id == Guid.Empty)
                    {
                        continue;
                    }
                }

                var normalized = Math.Clamp(scoreDoc.Score / maxScore, 0f, 1f);
                results.Add(new LuceneHit(id, scoreDoc.Score, normalized, doc));
            }

            return results;
        }
        catch (ParseException ex)
        {
            _logger.LogWarning(ex, "Lucene query parsing failed for raw query '{Query}'", plan.RawQueryText);
            return new List<LuceneHit>();
        }
        finally
        {
            stopwatch.Stop();
            _telemetry.RecordTrigramQuery(stopwatch.Elapsed);
        }
    }

    private Query? BuildQuery(SearchQueryPlan plan)
    {
        var raw = plan.RawQueryText;
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = plan.TrigramExpression;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var fields = new[]
        {
            LuceneIndexManager.FieldNames.Title,
            LuceneIndexManager.FieldNames.MetadataText,
            LuceneIndexManager.FieldNames.FileNameSearch,
            LuceneIndexManager.FieldNames.CatchAll,
            LuceneIndexManager.FieldNames.Author,
        };

        var parser = new MultiFieldQueryParser(LuceneVersion.LUCENE_48, fields, _indexManager.Analyzer)
        {
            DefaultOperator = Operator.OR,
        };
        try
        {
            return parser.Parse(raw);
        }
        catch (ParseException)
        {
            var escaped = QueryParserBase.Escape(raw);
            return parser.Parse(escaped);
        }
    }

    private static Guid ExtractId(IndexSearcher searcher, int docId)
    {
        var document = searcher.Doc(docId);
        if (Guid.TryParse(document.Get(LuceneIndexManager.FieldNames.Id), out var parsed))
        {
            return parsed;
        }

        return Guid.Empty;
    }

    private sealed class LuceneHit
    {
        public LuceneHit(Guid id, double rawScore, double normalizedScore, Document? document)
        {
            Id = id;
            Score = normalizedScore;
            RawScore = rawScore;
            Document = document;
        }

        public Guid Id { get; }

        public double Score { get; }

        public double RawScore { get; }

        public Document? Document { get; }

        public SearchHit ToSearchHit()
        {
            var doc = Document;
            if (doc is null)
            {
                return new SearchHit(
                    Id,
                    RawScore,
                    SourceName,
                    null,
                    string.Empty,
                    new List<HighlightSpan>(),
                    new Dictionary<string, string?>(),
                    null);
            }

            var metadataText = doc.Get(LuceneIndexManager.FieldNames.MetadataTextStored) ?? string.Empty;
            var snippet = BuildSnippet(metadataText, DefaultSnippetLength);
            var fields = new Dictionary<string, string?>
            {
                ["title"] = doc.Get(LuceneIndexManager.FieldNames.Title),
                ["author"] = doc.Get(LuceneIndexManager.FieldNames.Author),
                ["mime"] = doc.Get(LuceneIndexManager.FieldNames.Mime),
                ["filename"] = doc.Get(LuceneIndexManager.FieldNames.FileName),
            };

            var modifiedUtc = ParseDate(doc.Get(LuceneIndexManager.FieldNames.ModifiedUtc));
            var sortValues = modifiedUtc.HasValue
                ? new SearchHitSortValues(modifiedUtc.Value, Score, RawScore)
                : null;

            return new SearchHit(
                Id,
                RawScore,
                SourceName,
                LuceneIndexManager.FieldNames.MetadataText,
                snippet,
                new List<HighlightSpan>(),
                fields,
                sortValues);
        }

        private static string BuildSnippet(string source, int length)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return string.Empty;
            }

            var trimmed = source.Trim();
            if (trimmed.Length <= length)
            {
                return trimmed;
            }

            return trimmed[..Math.Min(length, trimmed.Length)] + "…";
        }

        private static DateTimeOffset? ParseDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
                ? dto
                : null;
        }
    }
}
