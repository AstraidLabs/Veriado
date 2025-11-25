using System.Threading;
using System.Threading.Tasks;

namespace Veriado.Appl.Abstractions;

public interface IApplicationMaintenanceCoordinator
{
    Task PauseBackgroundWorkAsync(CancellationToken cancellationToken = default);

    Task ResumeBackgroundWorkAsync(CancellationToken cancellationToken = default);

    Task WaitForResumeAsync(CancellationToken cancellationToken = default);
}
