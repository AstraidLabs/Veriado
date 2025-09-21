using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Veriado.Application.Search.Abstractions;
using Veriado.Application.UseCases.Queries;
using Veriado.Application.UseCases.Queries.FileGrid;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;
using Veriado.Services.Files.Models;

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

    public Task<PageResult<FileSummaryDto>> GetGridAsync(FileGridQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _mediator.Send(query, cancellationToken);
    }

    public Task<FileDetailDto?> GetDetailAsync(Guid fileId, CancellationToken cancellationToken)
    {
        return _mediator.Send(new GetFileDetailQuery(fileId), cancellationToken);
    }

    public Task<IReadOnlyList<SearchHistoryEntry>> GetSearchHistoryAsync(int take, CancellationToken cancellationToken)
    {
        return _historyService.GetRecentAsync(take, cancellationToken);
    }

    public Task<IReadOnlyList<SearchFavoriteItem>> GetFavoritesAsync(CancellationToken cancellationToken)
    {
        return _favoritesService.GetAllAsync(cancellationToken);
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
}
