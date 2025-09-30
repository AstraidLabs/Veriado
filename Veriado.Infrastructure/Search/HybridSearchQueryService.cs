using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Domain.Search;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Blends results from SQLite FTS5 and Lucene.Net to provide richer search experiences.
/// </summary>
internal sealed class HybridSearchQueryService : ISearchQueryService
{
    private const double LuceneWeight = 0.85d;

    private readonly SqliteFts5QueryService _ftsQueryService;
    private readonly LuceneSearchQueryService _luceneQueryService;

    public HybridSearchQueryService(SqliteFts5QueryService ftsQueryService, LuceneSearchQueryService luceneQueryService)
    {
        _ftsQueryService = ftsQueryService ?? throw new ArgumentNullException(nameof(ftsQueryService));
        _luceneQueryService = luceneQueryService ?? throw new ArgumentNullException(nameof(luceneQueryService));
    }

    public async Task<IReadOnlyList<(Guid Id, double Score)>> SearchWithScoresAsync(
        string matchQuery,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matchQuery);
        if (take <= 0)
        {
            return Array.Empty<(Guid, double)>();
        }

        if (skip < 0)
        {
            skip = 0;
        }

        var target = Math.Max(SafeAdd(skip, take), take);
        var (ftsHits, luceneHits) = await FetchOversampledAsync(
            (limit, ct) => _ftsQueryService.SearchWithScoresAsync(matchQuery, 0, limit, ct),
            (limit, ct) => _luceneQueryService.SearchWithScoresAsync(matchQuery, 0, limit, ct),
            CountUniqueScoreHits,
            target,
            cancellationToken).ConfigureAwait(false);

        return MergeScoreResults(ftsHits, luceneHits, skip, take);
    }

    public async Task<IReadOnlyList<(Guid Id, double Score)>> SearchFuzzyWithScoresAsync(
        string matchQuery,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matchQuery);
        if (take <= 0)
        {
            return Array.Empty<(Guid, double)>();
        }

        if (skip < 0)
        {
            skip = 0;
        }

        var target = Math.Max(SafeAdd(skip, take), take);
        var (ftsHits, luceneHits) = await FetchOversampledAsync(
            (limit, ct) => _ftsQueryService.SearchFuzzyWithScoresAsync(matchQuery, 0, limit, ct),
            (limit, ct) => _luceneQueryService.SearchFuzzyWithScoresAsync(matchQuery, 0, limit, ct),
            CountUniqueScoreHits,
            target,
            cancellationToken).ConfigureAwait(false);

        return MergeScoreResults(ftsHits, luceneHits, skip, take);
    }

    public async Task<int> CountAsync(string matchQuery, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matchQuery);
        var ftsCount = await _ftsQueryService.CountAsync(matchQuery, cancellationToken).ConfigureAwait(false);
        var luceneCount = await _luceneQueryService.CountAsync(matchQuery, cancellationToken).ConfigureAwait(false);
        return Math.Max(ftsCount, luceneCount);
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int? limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var take = limit.GetValueOrDefault(10);
        if (take <= 0)
        {
            return Array.Empty<SearchHit>();
        }

        var (ftsHits, luceneHits) = await FetchOversampledAsync(
            (limit, ct) => _ftsQueryService.SearchAsync(query, limit, ct),
            (limit, ct) => _luceneQueryService.SearchAsync(query, limit, ct),
            CountUniqueSearchHits,
            take,
            cancellationToken).ConfigureAwait(false);

        if (ftsHits.Count == 0 && luceneHits.Count == 0)
        {
            return Array.Empty<SearchHit>();
        }

        return MergeSearchHits(ftsHits, luceneHits, take);
    }

    private static IReadOnlyList<(Guid Id, double Score)> MergeScoreResults(
        IReadOnlyList<(Guid Id, double Score)> ftsHits,
        IReadOnlyList<(Guid Id, double Score)> luceneHits,
        int skip,
        int take)
    {
        if (ftsHits.Count == 0 && luceneHits.Count == 0)
        {
            return Array.Empty<(Guid, double)>();
        }

        var combined = new Dictionary<Guid, double>();
        foreach (var hit in luceneHits)
        {
            var weighted = hit.Score * LuceneWeight;
            if (combined.TryGetValue(hit.Id, out var current))
            {
                combined[hit.Id] = Math.Max(current, weighted);
            }
            else
            {
                combined[hit.Id] = weighted;
            }
        }

        foreach (var hit in ftsHits)
        {
            if (combined.TryGetValue(hit.Id, out var current))
            {
                combined[hit.Id] = Math.Max(current, hit.Score);
            }
            else
            {
                combined[hit.Id] = hit.Score;
            }
        }

        if (combined.Count == 0)
        {
            return Array.Empty<(Guid, double)>();
        }

        var ordered = combined
            .OrderByDescending(static pair => pair.Value)
            .ThenBy(static pair => pair.Key)
            .ToList();

        if (skip >= ordered.Count)
        {
            return Array.Empty<(Guid, double)>();
        }

        return ordered
            .Skip(skip)
            .Take(take)
            .Select(static pair => (pair.Key, pair.Value))
            .ToList();
    }

    private static IReadOnlyList<SearchHit> MergeSearchHits(
        IReadOnlyList<SearchHit> ftsHits,
        IReadOnlyList<SearchHit> luceneHits,
        int take)
    {
        var combined = new Dictionary<Guid, (SearchHit Hit, double Score, bool Highlight)>();

        foreach (var hit in ftsHits)
        {
            var hasHighlight = ContainsHighlight(hit.Title);
            combined[hit.FileId] = (hit, hit.Score, hasHighlight);
        }

        foreach (var hit in luceneHits)
        {
            var weightedScore = hit.Score * LuceneWeight;
            var hasHighlight = ContainsHighlight(hit.Title);
            if (combined.TryGetValue(hit.FileId, out var existing))
            {
                var bestScore = Math.Max(existing.Score, weightedScore);
                var snippet = !string.IsNullOrWhiteSpace(existing.Hit.Snippet)
                    ? existing.Hit.Snippet
                    : hit.Snippet;
                var title = existing.Highlight ? existing.Hit.Title : hit.Title;
                var updated = existing.Hit with
                {
                    Title = title,
                    Snippet = snippet,
                    Score = bestScore,
                };
                combined[hit.FileId] = (updated, bestScore, existing.Highlight || hasHighlight);
            }
            else
            {
                var updated = hit with { Score = weightedScore };
                combined[hit.FileId] = (updated, weightedScore, hasHighlight);
            }
        }

        if (combined.Count == 0)
        {
            return Array.Empty<SearchHit>();
        }

        return combined
            .Values
            .OrderByDescending(static item => item.Score)
            .ThenByDescending(static item => item.Highlight)
            .ThenByDescending(static item => item.Hit.LastModifiedUtc)
            .ThenBy(static item => item.Hit.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Hit.FileId)
            .Take(take)
            .Select(static item => item.Hit with { Score = item.Score })
            .ToList();
    }

    private async Task<(IReadOnlyList<T> Fts, IReadOnlyList<T> Lucene)> FetchOversampledAsync<T>(
        Func<int, CancellationToken, Task<IReadOnlyList<T>>> ftsFetcher,
        Func<int, CancellationToken, Task<IReadOnlyList<T>>> luceneFetcher,
        Func<IReadOnlyList<T>, IReadOnlyList<T>, int> uniqueCounter,
        int target,
        CancellationToken cancellationToken)
    {
        var desired = Math.Max(target, 1);
        var oversample = CalculateOversample(desired);
        var limit = oversample;
        var maxLimit = (int)Math.Min(int.MaxValue, (long)oversample * 3);
        if (maxLimit < oversample)
        {
            maxLimit = oversample;
        }

        IReadOnlyList<T> ftsHits = Array.Empty<T>();
        IReadOnlyList<T> luceneHits = Array.Empty<T>();
        var previousCount = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ftsHits = await ftsFetcher(limit, cancellationToken).ConfigureAwait(false);
            luceneHits = await luceneFetcher(limit, cancellationToken).ConfigureAwait(false);

            var uniqueCount = uniqueCounter(ftsHits, luceneHits);
            if (uniqueCount >= desired)
            {
                break;
            }

            var ftsExhausted = ftsHits.Count < limit;
            var luceneExhausted = luceneHits.Count < limit;
            if ((ftsExhausted && luceneExhausted) || (uniqueCount <= previousCount && (ftsExhausted || luceneExhausted)))
            {
                break;
            }

            if (limit >= maxLimit)
            {
                break;
            }

            previousCount = uniqueCount;
            var nextLimit = (long)limit + oversample;
            limit = nextLimit > maxLimit ? maxLimit : (int)nextLimit;
        }

        return (ftsHits, luceneHits);
    }

    private static int CountUniqueScoreHits(
        IReadOnlyList<(Guid Id, double Score)> ftsHits,
        IReadOnlyList<(Guid Id, double Score)> luceneHits)
    {
        var set = new HashSet<Guid>();
        foreach (var hit in ftsHits)
        {
            set.Add(hit.Id);
        }

        foreach (var hit in luceneHits)
        {
            set.Add(hit.Id);
        }

        return set.Count;
    }

    private static int CountUniqueSearchHits(
        IReadOnlyList<SearchHit> ftsHits,
        IReadOnlyList<SearchHit> luceneHits)
    {
        var set = new HashSet<Guid>();
        foreach (var hit in ftsHits)
        {
            set.Add(hit.FileId);
        }

        foreach (var hit in luceneHits)
        {
            set.Add(hit.FileId);
        }

        return set.Count;
    }

    private static int CalculateOversample(int target)
    {
        if (target <= 0)
        {
            return 1;
        }

        var oversample = (int)Math.Min((long)target * 3, int.MaxValue);
        return Math.Max(target, oversample);
    }

    private static bool ContainsHighlight(string title)
        => !string.IsNullOrEmpty(title) && title.IndexOf('[', StringComparison.Ordinal) >= 0;

    private static int SafeAdd(int left, int right)
    {
        var sum = (long)left + right;
        return sum > int.MaxValue ? int.MaxValue : (int)sum;
    }
}
