using System.Threading;
using System.Threading.Tasks;

namespace Veriado.Application.Abstractions;

/// <summary>
/// Coordinates application-wide maintenance periods by pausing and resuming
/// background processing so that long-running jobs can complete safely.
///
/// Implementations are expected to block background operations when
/// <see cref="PauseBackgroundWorkAsync"/> is invoked, and release the block once
/// <see cref="ResumeBackgroundWorkAsync"/> is called. Consumers can await
/// <see cref="WaitForResumeAsync"/> to be notified when maintenance is over.
/// </summary>
public interface IApplicationMaintenanceCoordinator
{
    /// <summary>
    /// Requests a pause of background work to allow maintenance to run without
    /// contention.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the pause request.</param>
    Task PauseBackgroundWorkAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Signals that maintenance work has finished and background processing may
    /// resume.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the resume request.</param>
    Task ResumeBackgroundWorkAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits until maintenance has completed and normal operations can continue.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the wait.</param>
    Task WaitForResumeAsync(CancellationToken cancellationToken = default);
}
