namespace Veriado.Services.Maintenance;

/// <summary>
/// Provides orchestration helpers for infrastructure and index maintenance tasks.
/// </summary>
public interface IMaintenanceService
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<AppResult<int>> VerifyAndRepairAsync(bool forceRepair, CancellationToken cancellationToken);

    Task<AppResult<int>> RunVacuumAndOptimizeAsync(CancellationToken cancellationToken);

    Task<AppResult<int>> ReindexAfterSchemaUpgradeAsync(int targetSchemaVersion, CancellationToken cancellationToken);
}
