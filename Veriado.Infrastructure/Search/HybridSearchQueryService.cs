using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Appl.Search;
using Veriado.Domain.Search;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides FTS5-backed search capabilities without trigram fallbacks.
/// </summary>
internal sealed class HybridSearchQueryService : ISearchQueryService
{
    private readonly SqliteFts5QueryService _ftsService;
    private readonly ISearchTelemetry _telemetry;

    public HybridSearchQueryService(
        SqliteFts5QueryService ftsService,
        ISearchTelemetry telemetry)
    {
        _ftsService = ftsService ?? throw new ArgumentNullException(nameof(ftsService));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
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
        return _ftsService.SearchWithScoresAsync(plan, skip, take, cancellationToken);
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

        var stopwatch = Stopwatch.StartNew();
        var take = limit.GetValueOrDefault(10);
        if (take <= 0)
        {
            stopwatch.Stop();
            _telemetry.RecordSearchLatency(stopwatch.Elapsed);
            return Array.Empty<SearchHit>();
        }

        var result = await _ftsService.SearchAsync(plan, take, cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();
        _telemetry.RecordSearchLatency(stopwatch.Elapsed);

        return result.Hits;
    }
}
