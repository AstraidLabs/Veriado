using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services;

internal sealed class HostShutdownService : IHostShutdownService
{
    private readonly ILogger<HostShutdownService> _logger;
    private IHost? _host;

    public HostShutdownService(ILogger<HostShutdownService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Initialize(IHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (Interlocked.CompareExchange(ref _host, host, null) is not null)
        {
            throw new InvalidOperationException("The host has already been initialized.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        var host = _host ?? throw new InvalidOperationException("The host has not been initialized.");
        return host.StopAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        var host = _host;
        if (host is null)
        {
            _logger.LogDebug("DisposeAsync called without an initialized host.");
            return;
        }

        if (host is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            host.Dispose();
        }
    }
}
