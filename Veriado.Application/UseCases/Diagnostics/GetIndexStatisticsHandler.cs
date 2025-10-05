namespace Veriado.Appl.UseCases.Diagnostics;

/// <summary>
/// Handles retrieval of search index diagnostics.
/// </summary>
public sealed class GetIndexStatisticsHandler : IRequestHandler<GetIndexStatisticsQuery, AppResult<IndexStatisticsDto>>
{
    private readonly IDiagnosticsRepository _diagnosticsRepository;

    public GetIndexStatisticsHandler(IDiagnosticsRepository diagnosticsRepository)
    {
        _diagnosticsRepository = diagnosticsRepository;
    }

    public async Task<AppResult<IndexStatisticsDto>> Handle(GetIndexStatisticsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _diagnosticsRepository.GetIndexStatisticsAsync(cancellationToken).ConfigureAwait(false);
            var dto = new IndexStatisticsDto(snapshot.TotalDocuments, snapshot.StaleDocuments, snapshot.LuceneVersion);
            return AppResult<IndexStatisticsDto>.Success(dto);
        }
        catch (Exception ex)
        {
            return AppResult<IndexStatisticsDto>.FromException(ex, "Failed to retrieve search index statistics.");
        }
    }
}
