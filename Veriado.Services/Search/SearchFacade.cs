using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Appl.Search.Abstractions;
using Veriado.Contracts.Search;

namespace Veriado.Services.Search;

public sealed class SearchFacade : ISearchFacade
{
    private readonly ISearchQueryService _searchQueryService;
    private readonly ISearchHistoryService _historyService;
    private readonly ISearchFavoritesService _favoritesService;

    public SearchFacade(
        ISearchQueryService searchQueryService,
        ISearchHistoryService historyService,
        ISearchFavoritesService favoritesService)
    {
        _searchQueryService = searchQueryService ?? throw new ArgumentNullException(nameof(searchQueryService));
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        _favoritesService = favoritesService ?? throw new ArgumentNullException(nameof(favoritesService));
    }

    public async Task<IReadOnlyList<SearchHitDto>> SearchAsync(string query, int take, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (take <= 0)
        {
            return Array.Empty<SearchHitDto>();
        }

        var hits = await _searchQueryService.SearchAsync(query, take, ct).ConfigureAwait(false);
        if (hits.Count == 0)
        {
            return Array.Empty<SearchHitDto>();
        }

        var results = new List<SearchHitDto>(hits.Count);
        foreach (var hit in hits)
        {
            results.Add(new SearchHitDto(hit.FileId, hit.Title, hit.Mime, hit.Snippet, hit.Score, hit.LastModifiedUtc));
        }

        return results;
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

        var totalCount = await _searchQueryService.CountAsync(favorite.MatchQuery, ct).ConfigureAwait(false);
        await _historyService
            .AddAsync(favorite.QueryText, favorite.MatchQuery, totalCount, favorite.IsFuzzy, ct)
            .ConfigureAwait(false);
    }

    public async Task AddToHistoryAsync(string query, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var totalCount = await _searchQueryService.CountAsync(query, ct).ConfigureAwait(false);
        await _historyService.AddAsync(query, query, totalCount, false, ct).ConfigureAwait(false);
    }
}
