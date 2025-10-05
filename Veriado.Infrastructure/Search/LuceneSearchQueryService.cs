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
using Veriado.Appl.Search.Abstractions;
using Veriado.Domain.Search;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Executes Lucene.NET based queries over the search index.
/// </summary>
internal sealed class LuceneSearchQueryService : ISearchQueryService
{
    private const string SourceName = "LUCENE";
    private const int DefaultSnippetLength = 240;

    private readonly LuceneIndexManager _indexManager;
    private readonly ISearchTelemetry _telemetry;
    private readonly ILogger<LuceneSearchQueryService> _logger;

    public LuceneSearchQueryService(
        LuceneIndexManager indexManager,
        ISearchTelemetry telemetry,
        ILogger<LuceneSearchQueryService> logger)
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

    public Task<IReadOnlyList<(Guid Id, double Score)>> SearchFuzzyWithScoresAsync(
        SearchQueryPlan plan,
        int skip,
        int take,
        CancellationToken cancellationToken)
        => SearchWithScoresAsync(plan, skip, take, cancellationToken);

    public async Task<int> CountAsync(SearchQueryPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
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
                return 0;
            }

            var docs = searcher.Search(query, 1);
            return (int)Math.Min(int.MaxValue, docs.TotalHits);
        }
        catch (ParseException ex)
        {
            _logger.LogWarning(ex, "Lucene query parsing failed for raw query '{Query}'", plan.RawQueryText);
            return 0;
        }
        finally
        {
            stopwatch.Stop();
            _telemetry.RecordSearchLatency(stopwatch.Elapsed);
        }
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        SearchQueryPlan plan,
        int? limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var take = limit.GetValueOrDefault(10);
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
                    if (!Guid.TryParse(doc.Get(SearchFieldNames.Id), out id))
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
            _telemetry.RecordSearchLatency(stopwatch.Elapsed);
        }
    }

    private Query? BuildQuery(SearchQueryPlan plan)
    {
        var matchExpression = plan.MatchExpression;
        var fallback = string.IsNullOrWhiteSpace(plan.RawQueryText) ? plan.TrigramExpression : plan.RawQueryText;
        var textToParse = string.IsNullOrWhiteSpace(matchExpression) ? fallback : matchExpression;

        if (string.IsNullOrWhiteSpace(textToParse) && plan.RangeFilters.Count == 0)
        {
            return null;
        }

        var fields = new[]
        {
            SearchFieldNames.Title,
            SearchFieldNames.MetadataText,
            SearchFieldNames.FileNameSearch,
            SearchFieldNames.CatchAll,
            SearchFieldNames.Author,
        };

        var parser = new MultiFieldQueryParser(LuceneVersion.LUCENE_48, fields, _indexManager.Analyzer)
        {
            DefaultOperator = Operator.OR,
        };

        Query baseQuery;
        if (string.IsNullOrWhiteSpace(textToParse))
        {
            baseQuery = new MatchAllDocsQuery();
        }
        else
        {
            baseQuery = ParseWithFallback(parser, textToParse);
        }

        return AttachRangeFilters(baseQuery, plan.RangeFilters);
    }

    private static Query ParseWithFallback(MultiFieldQueryParser parser, string expression)
    {
        try
        {
            return parser.Parse(expression);
        }
        catch (ParseException)
        {
            var escaped = QueryParserBase.Escape(expression);
            return parser.Parse(escaped);
        }
    }

    private static Query AttachRangeFilters(Query baseQuery, IReadOnlyList<SearchRangeFilter> filters)
    {
        if (filters is null || filters.Count == 0)
        {
            return baseQuery;
        }

        var boolean = new BooleanQuery { { baseQuery, Occur.MUST } };
        foreach (var filter in filters)
        {
            var rangeQuery = CreateRangeQuery(filter);
            if (rangeQuery is not null)
            {
                boolean.Add(rangeQuery, Occur.MUST);
            }
        }

        return boolean;
    }

    private static Query? CreateRangeQuery(SearchRangeFilter filter)
    {
        switch (filter.ValueKind)
        {
            case SearchRangeValueKind.Numeric:
                var lowerNumeric = ToNullableInt64(filter.LowerValue);
                var upperNumeric = ToNullableInt64(filter.UpperValue);
                if (lowerNumeric is null && upperNumeric is null)
                {
                    return null;
                }

                var includeLower = lowerNumeric.HasValue ? filter.IncludeLower : true;
                var includeUpper = upperNumeric.HasValue ? filter.IncludeUpper : true;
                return NumericRangeQuery.NewInt64Range(filter.Field, lowerNumeric, upperNumeric, includeLower, includeUpper);

            case SearchRangeValueKind.Lexicographic:
                var lowerText = filter.LowerValue as string;
                var upperText = filter.UpperValue as string;
                if (string.IsNullOrWhiteSpace(lowerText) && string.IsNullOrWhiteSpace(upperText))
                {
                    return null;
                }

                var includeLowerText = string.IsNullOrEmpty(lowerText) ? true : filter.IncludeLower;
                var includeUpperText = string.IsNullOrEmpty(upperText) ? true : filter.IncludeUpper;
                return TermRangeQuery.NewStringRange(filter.Field, lowerText, upperText, includeLowerText, includeUpperText);

            default:
                return null;
        }
    }

    private static long? ToNullableInt64(object? value)
    {
        return value switch
        {
            null => null,
            long l => l,
            int i => i,
            short s => s,
            byte b => b,
            uint ui => ui,
            ulong ul when ul <= long.MaxValue => (long)ul,
            string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    private static Guid ExtractId(IndexSearcher searcher, int docId)
    {
        var document = searcher.Doc(docId);
        if (Guid.TryParse(document.Get(SearchFieldNames.Id), out var parsed))
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

            var metadataText = doc.Get(SearchFieldNames.MetadataTextStored) ?? string.Empty;
            var snippet = BuildSnippet(metadataText, DefaultSnippetLength);
            var fields = new Dictionary<string, string?>
            {
                ["title"] = doc.Get(SearchFieldNames.Title),
                ["author"] = doc.Get(SearchFieldNames.Author),
                ["mime"] = doc.Get(SearchFieldNames.Mime),
                ["filename"] = doc.Get(SearchFieldNames.FileName),
            };

            var modifiedUtc = ParseDate(doc.Get(SearchFieldNames.ModifiedUtc));
            var sortValues = modifiedUtc.HasValue
                ? new SearchHitSortValues(modifiedUtc.Value, Score, RawScore)
                : null;

            return new SearchHit(
                Id,
                RawScore,
                SourceName,
                SearchFieldNames.MetadataText,
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
