using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Veriado.Application.UseCases.Queries;
using Veriado.Application.UseCases.Queries.FileGrid;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;
using Veriado.Contracts.Search;
using Veriado.Application.Search.Abstractions;
using Veriado.Services.Files.Models;
using AppSearchHistoryEntry = Veriado.Application.Search.Abstractions.SearchHistoryEntry;
using AppSearchFavoriteItem = Veriado.Application.Search.Abstractions.SearchFavoriteItem;

namespace Veriado.Services.Files;

/// <summary>
/// Implements read-oriented orchestration over the file catalogue.
/// </summary>
public sealed class FileQueryService : IFileQueryService
{
    private readonly IMediator _mediator;
    private readonly ISearchHistoryService _historyService;
    private readonly ISearchFavoritesService _favoritesService;

    public FileQueryService(
        IMediator mediator,
        ISearchHistoryService historyService,
        ISearchFavoritesService favoritesService)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        _favoritesService = favoritesService ?? throw new ArgumentNullException(nameof(favoritesService));
    }

    public Task<PageResult<FileSummaryDto>> GetGridAsync(FileGridQueryDto query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _mediator.Send(new FileGridQuery(query), cancellationToken);
    }

    public Task<FileDetailDto?> GetDetailAsync(Guid fileId, CancellationToken cancellationToken)
        => _mediator.Send(new GetFileDetailQuery(fileId), cancellationToken);

    public async Task<IReadOnlyList<SearchHistoryEntry>> GetSearchHistoryAsync(int take, CancellationToken cancellationToken)
    {
        var entries = await _historyService.GetRecentAsync(take, cancellationToken).ConfigureAwait(false);
        if (entries.Count == 0)
        {
            return Array.Empty<SearchHistoryEntry>();
        }

        var result = new List<SearchHistoryEntry>(entries.Count);
        foreach (var entry in entries)
        {
            result.Add(MapHistory(entry));
        }

        return result;
    }

    public async Task<IReadOnlyList<SearchFavoriteItem>> GetFavoritesAsync(CancellationToken cancellationToken)
    {
        var favorites = await _favoritesService.GetAllAsync(cancellationToken).ConfigureAwait(false);
        if (favorites.Count == 0)
        {
            return Array.Empty<SearchFavoriteItem>();
        }

        var result = new List<SearchFavoriteItem>(favorites.Count);
        foreach (var favorite in favorites)
        {
            result.Add(MapFavorite(favorite));
        }

        return result;
    }

    public Task AddFavoriteAsync(SearchFavoriteDefinition favorite, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(favorite);
        return _favoritesService.AddAsync(favorite.Name, favorite.MatchQuery, favorite.QueryText, favorite.IsFuzzy, cancellationToken);
    }

    public Task RemoveFavoriteAsync(Guid favoriteId, CancellationToken cancellationToken)
    {
        return _favoritesService.RemoveAsync(favoriteId, cancellationToken);
    }

    private static SearchHistoryEntry MapHistory(AppSearchHistoryEntry entry)
        => new(entry.Id, entry.QueryText, entry.MatchQuery, entry.LastQueriedUtc, entry.Executions, entry.LastTotalHits, entry.IsFuzzy);

    private static SearchFavoriteItem MapFavorite(AppSearchFavoriteItem favorite)
        => new(favorite.Id, favorite.Name, favorite.QueryText, favorite.MatchQuery, favorite.Position, favorite.CreatedUtc, favorite.IsFuzzy);
}
