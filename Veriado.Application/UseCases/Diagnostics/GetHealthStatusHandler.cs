namespace Veriado.Appl.UseCases.Diagnostics;

/// <summary>
/// Handles retrieval of database health diagnostics.
/// </summary>
public sealed class GetHealthStatusHandler : IRequestHandler<GetHealthStatusQuery, AppResult<HealthStatusDto>>
{
    private readonly IDiagnosticsRepository _diagnosticsRepository;

    public GetHealthStatusHandler(IDiagnosticsRepository diagnosticsRepository)
    {
        _diagnosticsRepository = diagnosticsRepository;
    }

    public async Task<AppResult<HealthStatusDto>> Handle(GetHealthStatusQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _diagnosticsRepository.GetDatabaseHealthAsync(cancellationToken).ConfigureAwait(false);
            var dto = new HealthStatusDto(snapshot.DatabasePath, snapshot.JournalMode, snapshot.IsWalEnabled, snapshot.PendingOutboxEvents);
            return AppResult<HealthStatusDto>.Success(dto);
        }
        catch (Exception ex)
        {
            return AppResult<HealthStatusDto>.FromException(ex, "Failed to retrieve database health information.");
        }
    }
}
