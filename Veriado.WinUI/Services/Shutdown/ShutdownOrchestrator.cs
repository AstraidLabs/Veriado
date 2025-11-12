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
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(10);

    private readonly IAppLifecycleService _lifecycleService;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly IHostShutdownService _hostShutdownService;
    private readonly ILogger<ShutdownOrchestrator> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool _stopped;
    private bool _disposed;
    private bool _disposing;

    public ShutdownOrchestrator(
        IAppLifecycleService lifecycleService,
        IHostApplicationLifetime applicationLifetime,
        IHostShutdownService hostShutdownService,
        ILogger<ShutdownOrchestrator> logger)
    {
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

            ExecuteStopApplication();

            var stopSucceeded = await StopLifecycleAsync(cancellationToken).ConfigureAwait(false);
            var hostResult = await _hostShutdownService
                .StopAndDisposeAsync(StopTimeout, DisposeTimeout, cancellationToken)
                .ConfigureAwait(false);

            LogHostShutdownResult(hostResult);

            if (stopSucceeded && hostResult.IsCompleted)
            {
                _logger.LogInformation("Shutdown sequence finished successfully.");
                _stopped = true;
                return ShutdownResult.Allow();
            }

            _logger.LogWarning(
                "Shutdown sequence incomplete. Lifecycle stopped: {LifecycleStopped}, host stop: {HostStopState}, host dispose: {HostDisposeState}.",
                stopSucceeded,
                hostResult.Stop.State,
                hostResult.Dispose.State);
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
            _stopped = true;
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

    private void LogHostShutdownResult(HostShutdownResult result)
    {
        if (result.IsCompleted)
        {
            _logger.LogInformation("Host stop/dispose completed successfully.");
            return;
        }

        switch (result.Stop.State)
        {
            case HostStopState.Completed:
            case HostStopState.AlreadyStopped:
            case HostStopState.NotInitialized:
                break;
            case HostStopState.Canceled:
                _logger.LogInformation("Host stop canceled via caller token.");
                break;
            case HostStopState.TimedOut:
                _logger.LogWarning(result.Stop.Exception, "Host stop timed out after {Timeout}.", StopTimeout);
                break;
            case HostStopState.Failed:
                _logger.LogError(result.Stop.Exception, "Host stop failed.");
                break;
        }

        switch (result.Dispose.State)
        {
            case HostDisposeState.Completed:
            case HostDisposeState.AlreadyDisposed:
            case HostDisposeState.NotInitialized:
                break;
            case HostDisposeState.Failed:
                _logger.LogError(result.Dispose.Exception, "Host dispose failed.");
                break;
        }
    }
}
