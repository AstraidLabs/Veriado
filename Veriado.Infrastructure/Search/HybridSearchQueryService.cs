using Veriado.Appl.Search;
using Veriado.Domain.Search;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Aggregates FTS5 and trigram search providers into a unified result set.
/// </summary>
internal sealed class HybridSearchQueryService : ISearchQueryService
{
    private readonly SqliteFts5QueryService _ftsService;
    private readonly TrigramQueryService _trigramService;

    public HybridSearchQueryService(SqliteFts5QueryService ftsService, TrigramQueryService trigramService)
    {
        _ftsService = ftsService ?? throw new ArgumentNullException(nameof(ftsService));
        _trigramService = trigramService ?? throw new ArgumentNullException(nameof(trigramService));
    }

    public Task<IReadOnlyList<(Guid Id, double Score)>> SearchWithScoresAsync(
        SearchQueryPlan plan,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        return _ftsService.SearchWithScoresAsync(plan, skip, take, cancellationToken);
    }

    public Task<IReadOnlyList<(Guid Id, double Score)>> SearchFuzzyWithScoresAsync(
        SearchQueryPlan plan,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        return _trigramService.SearchWithScoresAsync(plan, skip, take, cancellationToken);
    }

    public Task<int> CountAsync(SearchQueryPlan plan, CancellationToken cancellationToken)
    {
        return _ftsService.CountAsync(plan, cancellationToken);
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

        var oversample = Math.Max(take * 3, take);
        IReadOnlyList<SearchHit> ftsHits = Array.Empty<SearchHit>();
        if (!string.IsNullOrWhiteSpace(plan.MatchExpression))
        {
            ftsHits = await _ftsService.SearchAsync(plan, oversample, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<SearchHit> trigramHits = Array.Empty<SearchHit>();
        if (plan.RequiresTrigramFallback)
        {
            trigramHits = await _trigramService.SearchAsync(plan, oversample, cancellationToken).ConfigureAwait(false);
        }

        if (ftsHits.Count == 0 && trigramHits.Count == 0)
        {
            return Array.Empty<SearchHit>();
        }

        var combined = new Dictionary<Guid, CombinedHit>();
        var normalizedScores = new List<double>(ftsHits.Count);
        var queryText = plan.RawQueryText ?? plan.MatchExpression;

        foreach (var hit in ftsHits)
        {
            var normalized = ExtractNormalizedScore(hit.SortValues) ?? NormalizeScore(hit.Score);
            normalizedScores.Add(normalized);
            var lastModified = ExtractLastModified(hit.SortValues);
            combined[hit.Id] = new CombinedHit(hit, normalized, lastModified, HasExactTitle(hit, queryText));
        }

        var scale = ComputeScale(normalizedScores);
        foreach (var hit in trigramHits)
        {
            var normalized = ExtractNormalizedScore(hit.SortValues) ?? Math.Clamp(hit.Score, 0d, 1d);
            var scaled = Math.Clamp(normalized * scale, 0d, 1d);
            var lastModified = ExtractLastModified(hit.SortValues);

            if (combined.TryGetValue(hit.Id, out var existing))
            {
                var merged = MergeHits(existing.Hit, hit);
                var rankingScore = Math.Max(existing.RankingScore, scaled);
                var mergedSort = MergeSortValues(existing.Hit.SortValues, hit.SortValues, rankingScore, existing.Hit.Score, hit.Score, lastModified, existing.LastModified);
                combined[hit.Id] = existing with
                {
                    Hit = merged with { SortValues = mergedSort },
                    RankingScore = rankingScore,
                    LastModified = mergedSort?.LastModifiedUtc ?? existing.LastModified,
                    HasExactTitle = existing.HasExactTitle || HasExactTitle(hit, queryText),
                };
            }
            else
            {
                var sort = MergeSortValues(null, hit.SortValues, scaled, 0d, hit.Score, lastModified, null);
                combined[hit.Id] = new CombinedHit(hit with { SortValues = sort }, scaled, sort?.LastModifiedUtc ?? lastModified, HasExactTitle(hit, queryText));
            }
        }

        var ordered = combined.Values
            .OrderByDescending(static item => item.RankingScore)
            .ThenByDescending(static item => item.LastModified ?? DateTimeOffset.MinValue)
            .ThenBy(static item => item.HasExactTitle ? 0 : 1)
            .ThenBy(static item => item.Hit.Id)
            .Select(static item => item.Hit)
            .Take(take)
            .ToList();

        return ordered;
    }

    private static SearchHit MergeHits(SearchHit primary, SearchHit secondary)
    {
        var primaryField = string.IsNullOrWhiteSpace(primary.PrimaryField) ? secondary.PrimaryField : primary.PrimaryField;
        var snippet = string.IsNullOrWhiteSpace(primary.SnippetText) ? secondary.SnippetText : primary.SnippetText;
        var highlights = MergeHighlights(primary.Highlights, secondary.Highlights);
        var fields = MergeFields(primary.Fields, secondary.Fields);
        return primary with
        {
            PrimaryField = primaryField,
            SnippetText = snippet,
            Highlights = highlights,
            Fields = fields,
        };
    }

    private static IReadOnlyList<HighlightSpan> MergeHighlights(
        IReadOnlyList<HighlightSpan> primary,
        IReadOnlyList<HighlightSpan> secondary)
    {
        if (primary.Count == 0)
        {
            return secondary;
        }

        if (secondary.Count == 0)
        {
            return primary;
        }

        var set = new HashSet<HighlightSpan>(primary);
        var merged = new List<HighlightSpan>(primary);
        foreach (var span in secondary)
        {
            if (set.Add(span))
            {
                merged.Add(span);
            }
        }

        return merged;
    }

    private static IReadOnlyDictionary<string, string?> MergeFields(
        IReadOnlyDictionary<string, string?> primary,
        IReadOnlyDictionary<string, string?> secondary)
    {
        var merged = new Dictionary<string, string?>(primary, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in secondary)
        {
            if (!merged.ContainsKey(key) || string.IsNullOrWhiteSpace(merged[key]))
            {
                merged[key] = value;
            }
        }

        return merged;
    }

    private static double ComputeScale(IReadOnlyList<double> scores)
    {
        if (scores.Count == 0)
        {
            return 0.6d;
        }

        var ordered = scores.OrderBy(static value => value).ToArray();
        var midpoint = ordered.Length / 2;
        if (ordered.Length % 2 == 0)
        {
            return (ordered[midpoint - 1] + ordered[midpoint]) / 2d;
        }

        return ordered[midpoint];
    }

    private static SearchHitSortValues? MergeSortValues(
        object? primary,
        object? secondary,
        double rankingScore,
        double primaryRaw,
        double secondaryRaw,
        DateTimeOffset? secondaryLastModified,
        DateTimeOffset? primaryLastModified)
    {
        var primarySort = primary as SearchHitSortValues;
        var secondarySort = secondary as SearchHitSortValues;

        var lastModified = primaryLastModified ?? primarySort?.LastModifiedUtc ?? secondaryLastModified ?? secondarySort?.LastModifiedUtc;
        if (primarySort is not null && secondarySort is not null)
        {
            lastModified = primarySort.LastModifiedUtc >= secondarySort.LastModifiedUtc
                ? primarySort.LastModifiedUtc
                : secondarySort.LastModifiedUtc;
        }

        double raw;
        if (primarySort is not null)
        {
            raw = primarySort.RawScore;
        }
        else if (secondarySort is not null)
        {
            raw = secondarySort.RawScore;
        }
        else
        {
            raw = primaryRaw != 0d ? primaryRaw : secondaryRaw;
        }

        var secondaryScore = secondarySort?.NormalizedScore ?? secondaryRaw;
        return new SearchHitSortValues(lastModified ?? DateTimeOffset.MinValue, rankingScore, raw, secondaryScore);
    }

    private static double NormalizeScore(double rawScore)
    {
        if (double.IsNaN(rawScore) || double.IsInfinity(rawScore))
        {
            return 0d;
        }

        var clamped = Math.Max(0d, rawScore);
        return 1d / (1d + clamped);
    }

    private static double? ExtractNormalizedScore(object? sortValues)
    {
        return sortValues is SearchHitSortValues values ? values.NormalizedScore : null;
    }

    private static DateTimeOffset? ExtractLastModified(object? sortValues)
    {
        return sortValues is SearchHitSortValues values ? values.LastModifiedUtc : null;
    }

    private static bool HasExactTitle(SearchHit hit, string query)
    {
        if (!hit.Fields.TryGetValue("title", out var title) || string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        return string.Equals(title, query, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CombinedHit(SearchHit Hit, double RankingScore, DateTimeOffset? LastModified, bool HasExactTitle);
}
