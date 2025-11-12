using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.Services.Shutdown;

namespace Veriado.WinUI.Services;

internal sealed class HostShutdownService : IHostShutdownService
{
    private readonly IHostShutdownCoordinator _coordinator;
    private readonly ILogger<HostShutdownService> _logger;

    public HostShutdownService(IHostShutdownCoordinator coordinator, ILogger<HostShutdownService> logger)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task WhenStopped => _coordinator.WhenStopped;

    public void Initialize(IHost host)
    {
        _coordinator.Initialize(host);
        _logger.LogDebug("Host shutdown service initialized.");
    }

    public async Task<HostShutdownResult> StopAndDisposeAsync(
        TimeSpan stopTimeout,
        TimeSpan disposeTimeout,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _coordinator
                .StopAndDisposeAsync(stopTimeout, disposeTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Host shutdown request canceled via caller token.");
            throw;
        }
    }
}
