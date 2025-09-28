using Veriado.Appl.UseCases.Queries;
using Veriado.Appl.UseCases.Queries.FileGrid;
using Veriado.Appl.Search.Abstractions;

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

        return entries;
    }

    public async Task<IReadOnlyList<SearchFavoriteItem>> GetFavoritesAsync(CancellationToken cancellationToken)
    {
        var favorites = await _favoritesService.GetAllAsync(cancellationToken).ConfigureAwait(false);
        if (favorites.Count == 0)
        {
            return Array.Empty<SearchFavoriteItem>();
        }

        return favorites;
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
