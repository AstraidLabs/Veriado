using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Appl.Search.Abstractions;
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

        var fetch = Math.Max(skip + take, take);
        var ftsHits = await _ftsQueryService
            .SearchWithScoresAsync(matchQuery, 0, fetch, cancellationToken)
            .ConfigureAwait(false);
        var luceneHits = await _luceneQueryService
            .SearchWithScoresAsync(matchQuery, 0, fetch, cancellationToken)
            .ConfigureAwait(false);

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

        var fetch = Math.Max(skip + take, take);
        var ftsHits = await _ftsQueryService
            .SearchFuzzyWithScoresAsync(matchQuery, 0, fetch, cancellationToken)
            .ConfigureAwait(false);
        var luceneHits = await _luceneQueryService
            .SearchFuzzyWithScoresAsync(matchQuery, 0, fetch, cancellationToken)
            .ConfigureAwait(false);

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

        var fetch = take > int.MaxValue / 2 ? take : take * 2;
        var ftsHits = await _ftsQueryService.SearchAsync(query, fetch, cancellationToken).ConfigureAwait(false);
        var luceneHits = await _luceneQueryService.SearchAsync(query, fetch, cancellationToken).ConfigureAwait(false);

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
        var combined = new Dictionary<Guid, (SearchHit Hit, double Score)>();

        foreach (var hit in ftsHits)
        {
            combined[hit.FileId] = (hit, hit.Score);
        }

        foreach (var hit in luceneHits)
        {
            var weightedScore = hit.Score * LuceneWeight;
            if (combined.TryGetValue(hit.FileId, out var existing))
            {
                var bestScore = Math.Max(existing.Score, weightedScore);
                var snippet = !string.IsNullOrWhiteSpace(existing.Hit.Snippet)
                    ? existing.Hit.Snippet
                    : hit.Snippet;
                combined[hit.FileId] = (existing.Hit with { Score = bestScore, Snippet = snippet }, bestScore);
            }
            else
            {
                combined[hit.FileId] = (hit with { Score = weightedScore }, weightedScore);
            }
        }

        if (combined.Count == 0)
        {
            return Array.Empty<SearchHit>();
        }

        return combined
            .Values
            .OrderByDescending(static item => item.Score)
            .ThenBy(static item => item.Hit.FileId)
            .Take(take)
            .Select(static item => item.Hit with { Score = item.Score })
            .ToList();
    }
}
