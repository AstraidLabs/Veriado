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

    public SearchFacade(
        ISearchQueryService searchQueryService,
        ISearchHistoryService historyService,
        ISearchFavoritesService favoritesService,
        IMapper mapper)
    {
        _searchQueryService = searchQueryService ?? throw new ArgumentNullException(nameof(searchQueryService));
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        _favoritesService = favoritesService ?? throw new ArgumentNullException(nameof(favoritesService));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    public async Task<IReadOnlyList<SearchHitDto>> SearchAsync(string query, int take, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (take <= 0)
        {
            return Array.Empty<SearchHitDto>();
        }

        var plan = SearchQueryPlanFactory.FromMatch(query, query);
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

        var plan = favorite.IsFuzzy
            ? SearchQueryPlanFactory.FromTrigram(favorite.MatchQuery, favorite.QueryText)
            : SearchQueryPlanFactory.FromMatch(favorite.MatchQuery, favorite.QueryText);
        var totalCount = await _searchQueryService.CountAsync(plan, ct).ConfigureAwait(false);
        await _historyService
            .AddAsync(favorite.QueryText, favorite.MatchQuery, totalCount, favorite.IsFuzzy, ct)
            .ConfigureAwait(false);
    }

    public async Task AddToHistoryAsync(string query, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (!FtsQueryBuilder.TryBuild(query, prefix: true, allTerms: false, out var matchQuery))
        {
            return;
        }

        var plan = SearchQueryPlanFactory.FromMatch(matchQuery, query);
        var totalCount = await _searchQueryService.CountAsync(plan, ct).ConfigureAwait(false);
        await _historyService.AddAsync(query, matchQuery, totalCount, false, ct).ConfigureAwait(false);
    }
}
