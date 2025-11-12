using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veriado.Infrastructure.Lifecycle;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services.Shutdown;

public sealed class ShutdownOrchestrator : IShutdownOrchestrator, IAsyncDisposable
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(20);

    private readonly IConfirmService _confirmService;
    private readonly IAppLifecycleService _lifecycleService;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly IHostShutdownService _hostShutdownService;
    private readonly ILogger<ShutdownOrchestrator> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool _stopped;
    private bool _disposed;
    private bool _disposing;

    public ShutdownOrchestrator(
        IConfirmService confirmService,
        IAppLifecycleService lifecycleService,
        IHostApplicationLifetime applicationLifetime,
        IHostShutdownService hostShutdownService,
        ILogger<ShutdownOrchestrator> logger)
    {
        _confirmService = confirmService ?? throw new ArgumentNullException(nameof(confirmService));
        _lifecycleService = lifecycleService ?? throw new ArgumentNullException(nameof(lifecycleService));
        _applicationLifetime = applicationLifetime ?? throw new ArgumentNullException(nameof(applicationLifetime));
        _hostShutdownService = hostShutdownService ?? throw new ArgumentNullException(nameof(hostShutdownService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ShutdownResult> RequestAppShutdownAsync(
        ShutdownReason reason,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                _logger.LogDebug("Shutdown orchestrator already disposed; allowing close.");
                return ShutdownResult.Allow();
            }

            _logger.LogInformation("Shutdown requested with reason {Reason}.", reason);

            if (!await ConfirmAsync(reason, cancellationToken).ConfigureAwait(true))
            {
                _logger.LogInformation("Shutdown canceled by user confirmation.");
                return ShutdownResult.Cancel();
            }

            ExecuteStopApplication();

            var stopSucceeded = await StopLifecycleAsync(cancellationToken).ConfigureAwait(false);
            var hostStopped = await StopHostAsync(cancellationToken).ConfigureAwait(false);
            var disposeSucceeded = await DisposeHostAsync().ConfigureAwait(false);

            if (stopSucceeded && hostStopped && disposeSucceeded)
            {
                _logger.LogInformation("Shutdown sequence finished successfully.");
                _stopped = true;
                return ShutdownResult.Allow();
            }

            _logger.LogWarning(
                "Shutdown sequence incomplete. Lifecycle stopped: {LifecycleStopped}, host stopped: {HostStopped}, disposed: {Disposed}.",
                stopSucceeded,
                hostStopped,
                disposeSucceeded);
            return ShutdownResult.Allow();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Shutdown canceled via caller token.");
            return ShutdownResult.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected shutdown failure. Allowing window to close to avoid trapping the user.");
            return ShutdownResult.Allow();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposing)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposing)
            {
                return;
            }

            _disposing = true;
            _disposed = true;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task<bool> ConfirmAsync(ShutdownReason reason, CancellationToken cancellationToken)
    {
        if (reason != ShutdownReason.AppWindowClosing)
        {
            return true;
        }

        var options = new ConfirmOptions
        {
            Timeout = Timeout.InfiniteTimeSpan,
            CancellationToken = cancellationToken,
        };

        return await _confirmService
            .TryConfirmAsync("Ukončit aplikaci?", "Opravdu si přejete ukončit aplikaci?", "Ukončit", "Zůstat", options)
            .ConfigureAwait(true);
    }

    private void ExecuteStopApplication()
    {
        try
        {
            _applicationLifetime.StopApplication();
            _logger.LogDebug("Signaled host application lifetime to stop.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to signal application lifetime stop.");
        }
    }

    private async Task<bool> StopLifecycleAsync(CancellationToken cancellationToken)
    {
        if (_stopped)
        {
            _logger.LogDebug("Lifecycle already stopped.");
            return true;
        }

        using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stopCts.CancelAfter(StopTimeout);

        try
        {
            await _lifecycleService.StopAsync(stopCts.Token).ConfigureAwait(false);
            _logger.LogInformation("Lifecycle stopped cooperatively.");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Lifecycle stop canceled via caller token.");
            return false;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Lifecycle stop timed out after {Timeout}.", StopTimeout);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lifecycle stop failed.");
            return false;
        }
    }

    private async Task<bool> StopHostAsync(CancellationToken cancellationToken)
    {
        var result = await _hostShutdownService
            .StopAsync(StopTimeout, cancellationToken)
            .ConfigureAwait(false);

        switch (result.State)
        {
            case HostStopState.Completed:
                _logger.LogInformation("Host stopped successfully.");
                return true;
            case HostStopState.AlreadyStopped:
                _logger.LogDebug("Host stop skipped because it was already completed.");
                return true;
            case HostStopState.NotInitialized:
                _logger.LogDebug("Host stop skipped because host not initialized.");
                return true;
            case HostStopState.Canceled:
                _logger.LogInformation("Host stop canceled via caller token.");
                return false;
            case HostStopState.TimedOut:
                _logger.LogWarning("Host stop timed out after {Timeout}.", StopTimeout);
                return false;
            case HostStopState.Failed:
                _logger.LogError(result.Exception, "Host stop failed.");
                return false;
            default:
                return false;
        }
    }

    private async Task<bool> DisposeHostAsync()
    {
        var result = await _hostShutdownService.DisposeAsync().ConfigureAwait(false);

        switch (result.State)
        {
            case HostDisposeState.Completed:
                _logger.LogInformation("Host disposed successfully.");
                return true;
            case HostDisposeState.AlreadyDisposed:
                _logger.LogDebug("Host dispose skipped because host already disposed.");
                return true;
            case HostDisposeState.NotInitialized:
                _logger.LogDebug("Host dispose skipped because host not initialized.");
                return true;
            case HostDisposeState.Failed:
                _logger.LogError(result.Exception, "Host dispose failed.");
                return false;
            default:
                return false;
        }
    }
}
