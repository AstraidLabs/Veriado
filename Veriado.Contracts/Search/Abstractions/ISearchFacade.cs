using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Veriado.Contracts.Search.Abstractions;

public interface ISearchFacade
{
    Task<IReadOnlyList<SearchHitDto>> SearchAsync(string query, int take, CancellationToken ct);

    Task<IReadOnlyList<SearchHistoryEntry>> GetHistoryAsync(int take, CancellationToken ct);

    Task<IReadOnlyList<SearchFavoriteItem>> GetFavoritesAsync(CancellationToken ct);

    Task UseFavoriteAsync(SearchFavoriteDefinition favorite, CancellationToken ct);

    Task AddToHistoryAsync(string query, CancellationToken ct);
}
