using Microsoft.Extensions.Logging;

namespace Veriado.Appl.UseCases.Maintenance;

/// <summary>
/// Executes a full rebuild of the SQLite full-text index.
/// </summary>
public sealed class RebuildFulltextIndexHandler : IRequestHandler<RebuildFulltextIndexCommand, AppResult<int>>
{
    private readonly IDatabaseMaintenanceService _maintenanceService;
    private readonly ILogger<RebuildFulltextIndexHandler> _logger;

    public RebuildFulltextIndexHandler(
        IDatabaseMaintenanceService maintenanceService,
        ILogger<RebuildFulltextIndexHandler> logger)
    {
        _maintenanceService = maintenanceService;
        _logger = logger;
    }

    public async Task<AppResult<int>> Handle(RebuildFulltextIndexCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var executed = await _maintenanceService
                .RebuildFulltextIndexAsync(cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation("Full-text index rebuild completed successfully.");
            return AppResult<int>.Success(executed);
        }
        catch (Exception ex)
        {
            return AppResult<int>.FromException(ex, "Failed to rebuild the full-text index.");
        }
    }
}
