namespace Veriado.Appl.Abstractions;

/// <summary>
/// Provides database maintenance operations such as vacuuming and optimisation.
/// </summary>
public interface IDatabaseMaintenanceService
{
    /// <summary>
    /// Executes VACUUM and PRAGMA optimise commands.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of maintenance statements executed.</returns>
    Task<int> VacuumAndOptimizeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Rehydrates the SQLite write-ahead log to ensure consistent startup state.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task RehydrateWalAsync(CancellationToken cancellationToken);
}
