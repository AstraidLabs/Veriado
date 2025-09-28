using Veriado.Contracts.Diagnostics;

namespace Veriado.Services.Diagnostics;

/// <summary>
/// Provides diagnostic information about infrastructure state and search index health.
/// </summary>
public interface IHealthService
{
    Task<AppResult<HealthStatusDto>> GetAsync(CancellationToken cancellationToken);

    Task<AppResult<IndexStatisticsDto>> GetIndexStatisticsAsync(CancellationToken cancellationToken);
}
