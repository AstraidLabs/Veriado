using System.Threading;
using System.Threading.Tasks;
using Veriado.Application.Abstractions;

namespace Veriado.Infrastructure.Maintenance;

public sealed class ApplicationMaintenanceCoordinator : IApplicationMaintenanceCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task PauseBackgroundWorkAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task ResumeBackgroundWorkAsync(CancellationToken cancellationToken = default)
    {
        _gate.Release();
        return Task.CompletedTask;
    }

    public async Task WaitForResumeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        _gate.Release();
    }
}
