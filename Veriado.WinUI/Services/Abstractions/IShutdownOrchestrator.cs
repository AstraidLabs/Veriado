using Veriado.WinUI.Services.Shutdown;

namespace Veriado.WinUI.Services.Abstractions;

public interface IShutdownOrchestrator
{
    Task<ShutdownResult> RequestAppShutdownAsync(
        ShutdownReason reason,
        CancellationToken cancellationToken = default);
}
