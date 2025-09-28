using Veriado.Appl.UseCases.Diagnostics;
using Veriado.Contracts.Diagnostics;

namespace Veriado.Services.Diagnostics;

/// <summary>
/// Implements diagnostic queries over the infrastructure state.
/// </summary>
public sealed class HealthService : IHealthService
{
    private readonly IMediator _mediator;

    public HealthService(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    public Task<AppResult<HealthStatusDto>> GetAsync(CancellationToken cancellationToken)
    {
        return _mediator.Send(new GetHealthStatusQuery(), cancellationToken);
    }

    public Task<AppResult<IndexStatisticsDto>> GetIndexStatisticsAsync(CancellationToken cancellationToken)
    {
        return _mediator.Send(new GetIndexStatisticsQuery(), cancellationToken);
    }
}
