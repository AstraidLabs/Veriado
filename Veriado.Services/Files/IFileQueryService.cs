using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Application.UseCases.Queries.FileGrid;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;
using Veriado.Contracts.Search;
using Veriado.Services.Files.Models;

namespace Veriado.Services.Files;

/// <summary>
/// Provides read-oriented orchestration services over the file catalogue.
/// </summary>
public interface IFileQueryService
{
    Task<PageResult<FileSummaryDto>> GetGridAsync(FileGridQuery query, CancellationToken cancellationToken);

    Task<FileDetailDto?> GetDetailAsync(Guid fileId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SearchHistoryEntry>> GetSearchHistoryAsync(int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<SearchFavoriteItem>> GetFavoritesAsync(CancellationToken cancellationToken);

    Task AddFavoriteAsync(SearchFavoriteDefinition favorite, CancellationToken cancellationToken);

    Task RemoveFavoriteAsync(Guid favoriteId, CancellationToken cancellationToken);
}
