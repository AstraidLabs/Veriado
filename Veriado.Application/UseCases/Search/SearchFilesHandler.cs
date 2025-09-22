using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using MediatR;
using Veriado.Application.Common;
using Veriado.Application.Search.Abstractions;
using Veriado.Contracts.Search;

namespace Veriado.Application.UseCases.Search;

/// <summary>
/// Handles full-text search queries for files.
/// </summary>
public sealed class SearchFilesHandler : IRequestHandler<SearchFilesQuery, IReadOnlyList<SearchHitDto>>
{
    private readonly ISearchQueryService _searchQueryService;
    private readonly IMapper _mapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchFilesHandler"/> class.
    /// </summary>
    public SearchFilesHandler(ISearchQueryService searchQueryService, IMapper mapper)
    {
        _searchQueryService = searchQueryService;
        _mapper = mapper;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchHitDto>> Handle(SearchFilesQuery request, CancellationToken cancellationToken)
    {
        Guard.AgainstNullOrWhiteSpace(request.Text, nameof(request.Text));
        var hits = await _searchQueryService.SearchAsync(request.Text, request.Limit, cancellationToken);
        return _mapper.Map<IReadOnlyList<SearchHitDto>>(hits);
    }
}
