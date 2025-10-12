using System;
using AutoMapper;
using Veriado.Appl.Search;
using Veriado.Appl.Search.Abstractions;
using Veriado.Contracts.Search.Abstractions;

namespace Veriado.Services.Search;

public sealed class SearchFacade : ISearchFacade
{
    private readonly ISearchQueryService _searchQueryService;
    private readonly ISearchHistoryService _historyService;
    private readonly ISearchFavoritesService _favoritesService;
    private readonly IMapper _mapper;
    private readonly IAnalyzerFactory _analyzerFactory;

    public SearchFacade(
        ISearchQueryService searchQueryService,
        ISearchHistoryService historyService,
        ISearchFavoritesService favoritesService,
        IMapper mapper,
        IAnalyzerFactory analyzerFactory)
    {
        _searchQueryService = searchQueryService ?? throw new ArgumentNullException(nameof(searchQueryService));
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        _favoritesService = favoritesService ?? throw new ArgumentNullException(nameof(favoritesService));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
    }

    public async Task<IReadOnlyList<SearchHitDto>> SearchAsync(string query, int take, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (take <= 0)
        {
            return Array.Empty<SearchHitDto>();
        }

        var match = FtsQueryBuilder.BuildMatch(query, prefix: false, allTerms: false, _analyzerFactory);
        if (string.IsNullOrWhiteSpace(match))
        {
            return Array.Empty<SearchHitDto>();
        }

        var plan = SearchQueryPlanFactory.FromMatch(match, query);
        var hits = await _searchQueryService.SearchAsync(plan, take, ct).ConfigureAwait(false);
        if (hits.Count == 0)
        {
            return Array.Empty<SearchHitDto>();
        }

        return _mapper.Map<IReadOnlyList<SearchHitDto>>(hits);
    }

    public async Task<IReadOnlyList<SearchHistoryEntry>> GetHistoryAsync(int take, CancellationToken ct)
    {
        var history = await _historyService.GetRecentAsync(take, ct).ConfigureAwait(false);
        return history.Count == 0 ? Array.Empty<SearchHistoryEntry>() : history;
    }

    public async Task<IReadOnlyList<SearchFavoriteItem>> GetFavoritesAsync(CancellationToken ct)
    {
        var favorites = await _favoritesService.GetAllAsync(ct).ConfigureAwait(false);
        return favorites.Count == 0 ? Array.Empty<SearchFavoriteItem>() : favorites;
    }

    public async Task UseFavoriteAsync(SearchFavoriteDefinition favorite, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(favorite);
        if (string.IsNullOrWhiteSpace(favorite.MatchQuery))
        {
            return;
        }

        var matchQuery = favorite.MatchQuery;
        if (favorite.IsFuzzy)
        {
            matchQuery = TryBuildMatchFromText(favorite.QueryText) ?? matchQuery;
        }

        if (string.IsNullOrWhiteSpace(matchQuery))
        {
            return;
        }

        var plan = SearchQueryPlanFactory.FromMatch(matchQuery, favorite.QueryText);
        var totalCount = await _searchQueryService.CountAsync(plan, ct).ConfigureAwait(false);
        await _historyService
            .AddAsync(favorite.QueryText, matchQuery, totalCount, false, ct)
            .ConfigureAwait(false);
    }

    public async Task AddToHistoryAsync(string query, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (!FtsQueryBuilder.TryBuild(query, prefix: true, allTerms: false, _analyzerFactory, out var matchQuery))
        {
            return;
        }

        var plan = SearchQueryPlanFactory.FromMatch(matchQuery, query);
        var totalCount = await _searchQueryService.CountAsync(plan, ct).ConfigureAwait(false);
        await _historyService.AddAsync(query, matchQuery, totalCount, false, ct).ConfigureAwait(false);
    }

    private string? TryBuildMatchFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return FtsQueryBuilder.TryBuild(text!, prefix: false, allTerms: false, _analyzerFactory, out var match)
            ? match
            : null;
    }
}
