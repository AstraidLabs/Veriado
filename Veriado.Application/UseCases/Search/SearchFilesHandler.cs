using System;
using AutoMapper;
using Veriado.Appl.Search;
using Veriado.Appl.Search.Abstractions;

namespace Veriado.Appl.UseCases.Search;

/// <summary>
/// Handles full-text search queries for files.
/// </summary>
public sealed class SearchFilesHandler : IRequestHandler<SearchFilesQuery, IReadOnlyList<SearchHitDto>>
{
    private readonly ISearchQueryService _searchQueryService;
    private readonly IMapper _mapper;
    private readonly IAnalyzerFactory _analyzerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchFilesHandler"/> class.
    /// </summary>
    public SearchFilesHandler(ISearchQueryService searchQueryService, IMapper mapper, IAnalyzerFactory analyzerFactory)
    {
        _searchQueryService = searchQueryService;
        _mapper = mapper;
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchHitDto>> Handle(SearchFilesQuery request, CancellationToken cancellationToken)
    {
        Guard.AgainstNullOrWhiteSpace(request.Text, nameof(request.Text));
        var match = FtsQueryBuilder.BuildMatch(request.Text, prefix: false, allTerms: false, _analyzerFactory);
        if (string.IsNullOrWhiteSpace(match))
        {
            return Array.Empty<SearchHitDto>();
        }

        var plan = SearchQueryPlanFactory.FromMatch(match, request.Text);
        var hits = await _searchQueryService.SearchAsync(plan, request.Limit, cancellationToken);
        return _mapper.Map<IReadOnlyList<SearchHitDto>>(hits);
    }
}
