using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services;

internal sealed class HostShutdownService : IHostShutdownService
{
    private static readonly TimeSpan DefaultDisposeStopTimeout = TimeSpan.FromSeconds(5);

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

    public async Task<HostStopResult> StopAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var host = Volatile.Read(ref _host);
        if (host is null)
        {
            _logger.LogDebug("Host stop skipped because host has not been initialized.");
            return HostStopResult.NotInitialized();
        }

        var result = await StopHostCoreAsync(host, timeout, cancellationToken, logOnSuccess: true).ConfigureAwait(false);
        return result;
    }

    public async ValueTask<HostDisposeResult> DisposeAsync()
    {
        if (IsDisposeCompleted)
        {
            _logger.LogDebug("Host dispose requested but already completed.");
            return HostDisposeResult.AlreadyDisposed();
        }

        var host = Interlocked.Exchange(ref _host, null);
        if (host is null)
        {
            _logger.LogDebug("DisposeAsync called without an initialized host.");
            Volatile.Write(ref _disposeCompleted, true);
            Volatile.Write(ref _stopCompleted, true);
            return HostDisposeResult.NotInitialized();
        }

        try
        {
            if (!IsStopCompleted)
            {
                var stopResult = await StopHostCoreAsync(host, DefaultDisposeStopTimeout, CancellationToken.None, logOnSuccess: false)
                    .ConfigureAwait(false);

                if (!stopResult.IsSuccess)
                {
                    _logger.LogWarning(
                        stopResult.Exception,
                        "Best-effort stop during dispose completed with status {Status}.",
                        stopResult.State);
                }
            }

            if (host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                host.Dispose();
            }

            _logger.LogInformation("Host disposed successfully.");
            Volatile.Write(ref _disposeCompleted, true);
            return HostDisposeResult.Completed();
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _disposeCompleted, false);
            _logger.LogError(ex, "Host dispose failed.");
            return HostDisposeResult.Failed(ex);
        }
    }

    private async Task<HostStopResult> StopHostCoreAsync(
        IHost host,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        bool logOnSuccess)
    {
        if (IsStopCompleted)
        {
            _logger.LogDebug("Host stop requested but already completed.");
            return HostStopResult.AlreadyStopped();
        }

        using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stopCts.CancelAfter(timeout);

        try
        {
            await host.StopAsync(stopCts.Token).ConfigureAwait(false);
            Volatile.Write(ref _stopCompleted, true);

            if (logOnSuccess)
            {
                _logger.LogInformation("Host stopped successfully.");
            }
            else
            {
                _logger.LogDebug("Host stopped as part of disposal.");
            }

            return HostStopResult.Completed();
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Host stop canceled via caller token.");
            return HostStopResult.Canceled(ex);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning("Host stop timed out after {Timeout}.", timeout);
            return HostStopResult.TimedOut(ex);
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Host stop skipped because host already disposed.");
            Volatile.Write(ref _stopCompleted, true);
            return HostStopResult.AlreadyStopped(ex);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "Host stop skipped because host not initialized.");
            Volatile.Write(ref _stopCompleted, true);
            return HostStopResult.NotInitialized(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Host stop failed.");
            return HostStopResult.Failed(ex);
        }
    }
}
