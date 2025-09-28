namespace Veriado.Appl.UseCases.Maintenance;

/// <summary>
/// Triggers maintenance commands that reclaim unused space and optimise the SQLite database.
/// </summary>
public sealed record VacuumAndOptimizeDatabaseCommand : IRequest<AppResult<int>>;
