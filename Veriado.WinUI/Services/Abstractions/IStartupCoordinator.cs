using System.Threading;
using System.Threading.Tasks;
using Veriado.WinUI.Views.Shell;

namespace Veriado.WinUI.Services.Abstractions;

public interface IStartupCoordinator
{
    Task<StartupResult> RunAsync(CancellationToken cancellationToken);
}

public sealed record StartupResult(MainShell Shell);
