using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Veriado.Application.Abstractions;
using Veriado.Application.Common;

namespace Veriado.Application.UseCases.Maintenance;

/// <summary>
/// Executes SQLite VACUUM and PRAGMA optimize commands to keep the database lean.
/// </summary>
public sealed class VacuumAndOptimizeDatabaseHandler : IRequestHandler<VacuumAndOptimizeDatabaseCommand, AppResult<int>>
{
    private readonly IDatabaseMaintenanceService _maintenanceService;
    private readonly ILogger<VacuumAndOptimizeDatabaseHandler> _logger;

    public VacuumAndOptimizeDatabaseHandler(
        IDatabaseMaintenanceService maintenanceService,
        ILogger<VacuumAndOptimizeDatabaseHandler> logger)
    {
        _maintenanceService = maintenanceService;
        _logger = logger;
    }

    public async Task<AppResult<int>> Handle(VacuumAndOptimizeDatabaseCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var executed = await _maintenanceService.VacuumAndOptimizeAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("SQLite vacuum and optimise completed");
            return AppResult<int>.Success(executed);
        }
        catch (Exception ex)
        {
            return AppResult<int>.FromException(ex, "Failed to vacuum and optimise the database.");
        }
    }
}
