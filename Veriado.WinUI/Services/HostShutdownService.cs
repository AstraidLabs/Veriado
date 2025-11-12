using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services;

internal sealed class HostShutdownService : IHostShutdownService
{
    private readonly ILogger<HostShutdownService> _logger;
    private IHost? _host;
    private volatile bool _stopCompleted;
    private volatile bool _disposeCompleted;

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

        Volatile.Write(ref _stopCompleted, false);
        Volatile.Write(ref _disposeCompleted, false);
    }

    public bool IsStopCompleted => Volatile.Read(ref _stopCompleted);

    public bool IsDisposeCompleted => Volatile.Read(ref _disposeCompleted);

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (IsStopCompleted)
        {
            _logger.LogDebug("Host stop requested but already completed.");
            return;
        }

        var host = _host ?? throw new InvalidOperationException("The host has not been initialized.");

        await host.StopAsync(cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _stopCompleted, true);
    }

    public async ValueTask DisposeAsync()
    {
        if (IsDisposeCompleted)
        {
            _logger.LogDebug("Host dispose requested but already completed.");
            return;
        }

        var host = Interlocked.Exchange(ref _host, null);
        if (host is null)
        {
            _logger.LogDebug("DisposeAsync called without an initialized host.");
            Volatile.Write(ref _disposeCompleted, true);
            Volatile.Write(ref _stopCompleted, true);
            return;
        }

        try
        {
            if (!IsStopCompleted)
            {
                _logger.LogDebug("Disposing host without prior StopAsync; signaling stop.");
                try
                {
                    using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await host.StopAsync(stopCts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Best-effort stop during dispose failed.");
                }

                Volatile.Write(ref _stopCompleted, true);
            }

            if (host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                host.Dispose();
            }

            _logger.LogDebug("Host disposed successfully.");
            Volatile.Write(ref _disposeCompleted, true);
        }
        catch
        {
            Volatile.Write(ref _disposeCompleted, false);
            throw;
        }
    }
}
