using System.Threading;
using System.Threading.Tasks;
using Veriado.Application.Common;

namespace Veriado.Services.Maintenance;

/// <summary>
/// Provides orchestration helpers for infrastructure and index maintenance tasks.
/// </summary>
public interface IMaintenanceService
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<AppResult<int>> VerifyAndRepairAsync(bool forceRepair, bool extractContent, CancellationToken cancellationToken);

    Task<AppResult<int>> RunVacuumAndOptimizeAsync(CancellationToken cancellationToken);

    Task<AppResult<int>> ReindexAfterSchemaUpgradeAsync(int targetSchemaVersion, bool extractContent, bool allowDeferredIndexing, CancellationToken cancellationToken);
}
