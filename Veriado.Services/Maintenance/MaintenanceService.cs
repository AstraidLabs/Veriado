using Veriado.Infrastructure.DependencyInjection;

namespace Veriado.Services.Maintenance;

/// <summary>
/// Implements orchestration over database and index maintenance workflows.
/// </summary>
public sealed class MaintenanceService : IMaintenanceService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IMediator _mediator;

    public MaintenanceService(IServiceProvider serviceProvider, IMediator mediator)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await RunVacuumAndOptimizeAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<AppResult<int>> VerifyAndRepairAsync(bool forceRepair, CancellationToken cancellationToken)
    {
        var command = new VerifyAndRepairFulltextCommand(forceRepair);
        return _mediator.Send(command, cancellationToken);
    }

    public Task<AppResult<int>> RunVacuumAndOptimizeAsync(CancellationToken cancellationToken)
    {
        return _mediator.Send(new VacuumAndOptimizeDatabaseCommand(), cancellationToken);
    }

    public Task<AppResult<int>> ReindexAfterSchemaUpgradeAsync(int targetSchemaVersion, bool allowDeferredIndexing, CancellationToken cancellationToken)
    {
        var command = new ReindexCorpusAfterSchemaUpgradeCommand(targetSchemaVersion, allowDeferredIndexing);
        return _mediator.Send(command, cancellationToken);
    }
}
