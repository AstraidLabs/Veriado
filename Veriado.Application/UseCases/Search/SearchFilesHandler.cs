using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Veriado.Application.Common;
using Veriado.Application.Mapping;
using Veriado.Application.Search.Abstractions;
using Veriado.Contracts.Search;

namespace Veriado.Application.UseCases.Search;

/// <summary>
/// Handles full-text search queries for files.
/// </summary>
public sealed class SearchFilesHandler : IRequestHandler<SearchFilesQuery, IReadOnlyList<SearchHitDto>>
{
    private readonly ISearchQueryService _searchQueryService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchFilesHandler"/> class.
    /// </summary>
    public SearchFilesHandler(ISearchQueryService searchQueryService)
    {
        _searchQueryService = searchQueryService;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchHitDto>> Handle(SearchFilesQuery request, CancellationToken cancellationToken)
    {
        Guard.AgainstNullOrWhiteSpace(request.Text, nameof(request.Text));
        var hits = await _searchQueryService.SearchAsync(request.Text, request.Limit, cancellationToken);
        return hits.Select(DomainToDto.ToSearchHitDto).ToArray();
    }
}
