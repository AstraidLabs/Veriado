using System;
using System.Threading;
using System.Threading.Tasks;

namespace Veriado.Infrastructure.Lifecycle;

public enum AppLifecycleState
{
    Stopped,
    Starting,
    Running,
    Pausing,
    Paused,
    Resuming,
    Restarting,
    Stopping,
    Faulted,
    Disposed,
}

public interface IAppLifecycleService
{
    AppLifecycleState State { get; }

    CancellationToken RunToken { get; }

    PauseToken PauseToken { get; }

    Task StartAsync(CancellationToken ct = default);

    Task StopAsync(CancellationToken ct = default);

    Task PauseAsync(CancellationToken ct = default);

    Task ResumeAsync(CancellationToken ct = default);

    Task RestartAsync(CancellationToken ct = default);
}
