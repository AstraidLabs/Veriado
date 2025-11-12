using System;
using System.Diagnostics;
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
        var stopwatch = Stopwatch.StartNew();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                _logger.LogDebug("Shutdown orchestrator already disposed; allowing close.");
                stopwatch.Stop();
                return ShutdownResult.Success(stopwatch.Elapsed, _stopped, HostShutdownResult.NotInitialized());
            }

            _logger.LogInformation("Shutdown requested with reason {Reason}.", reason);

            ExecuteStopApplication();

            var lifecycleOutcome = await StopLifecycleAsync(cancellationToken).ConfigureAwait(false);

            var hostResult = await _hostShutdownService
                .StopAndDisposeAsync(StopTimeout, DisposeTimeout, cancellationToken)
                .ConfigureAwait(false);

            LogHostShutdownResult(hostResult);

            if (lifecycleOutcome.Success && hostResult.IsCompleted)
            {
                _stopped = true;
                _disposed = true;
                stopwatch.Stop();
                _logger.LogInformation(
                    "Shutdown sequence finished successfully in {Duration}.",
                    stopwatch.Elapsed);
                return ShutdownResult.Success(stopwatch.Elapsed, lifecycleOutcome.Success, hostResult);
            }

            var failure = ResolveFailure(lifecycleOutcome, hostResult);
            stopwatch.Stop();

            _logger.LogWarning(
                "Shutdown sequence incomplete after {Duration}. Lifecycle stopped: {LifecycleStopped}; host stop: {HostStopState}; host dispose: {HostDisposeState}.",
                stopwatch.Elapsed,
                lifecycleOutcome.Success,
                hostResult.Stop.State,
                hostResult.Dispose.State);

            return ShutdownResult.Failure(failure, stopwatch.Elapsed, lifecycleOutcome.Success, hostResult);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Shutdown canceled via caller token.");
            stopwatch.Stop();
            return ShutdownResult.Canceled(stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Unexpected shutdown failure.");
            return ShutdownResult.Failure(
                ShutdownFailureDetail.Error(ShutdownFailurePhase.LifecycleStop, ex),
                stopwatch.Elapsed,
                lifecycleStopped: _stopped,
                host: default);
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

    private async Task<LifecycleStopOutcome> StopLifecycleAsync(CancellationToken cancellationToken)
    {
        if (_stopped)
        {
            _logger.LogDebug("Lifecycle already stopped.");
            return LifecycleStopOutcome.Success();
        }

        using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stopCts.CancelAfter(StopTimeout);

        try
        {
            await _lifecycleService.StopAsync(stopCts.Token).ConfigureAwait(false);
            _logger.LogInformation("Lifecycle stopped cooperatively.");
            _stopped = true;
            return LifecycleStopOutcome.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Lifecycle stop canceled via caller token.");
            return LifecycleStopOutcome.Canceled();
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning("Lifecycle stop timed out after {Timeout}.", StopTimeout);
            return LifecycleStopOutcome.Timeout(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lifecycle stop failed.");
            return LifecycleStopOutcome.Failed(ex);
        }
    }

    private static ShutdownFailureDetail ResolveFailure(LifecycleStopOutcome lifecycleOutcome, HostShutdownResult hostResult)
    {
        if (!lifecycleOutcome.Success)
        {
            return lifecycleOutcome.Failure ?? ShutdownFailureDetail.Unknown(ShutdownFailurePhase.LifecycleStop);
        }

        if (!hostResult.Stop.IsSuccess)
        {
            return hostResult.Stop.State switch
            {
                HostStopState.TimedOut => ShutdownFailureDetail.Timeout(ShutdownFailurePhase.HostStop, hostResult.Stop.Exception),
                HostStopState.Canceled => ShutdownFailureDetail.Canceled(ShutdownFailurePhase.HostStop, hostResult.Stop.Exception),
                HostStopState.Failed => ShutdownFailureDetail.Error(ShutdownFailurePhase.HostStop, hostResult.Stop.Exception),
                HostStopState.NotInitialized => ShutdownFailureDetail.NotSupported(ShutdownFailurePhase.HostStop),
                HostStopState.AlreadyStopped => ShutdownFailureDetail.Unknown(ShutdownFailurePhase.HostStop, hostResult.Stop.Exception),
                _ => ShutdownFailureDetail.Unknown(ShutdownFailurePhase.HostStop, hostResult.Stop.Exception),
            };
        }

        if (!hostResult.Dispose.IsSuccess)
        {
            return hostResult.Dispose.State switch
            {
                HostDisposeState.Failed => ShutdownFailureDetail.Error(ShutdownFailurePhase.HostDispose, hostResult.Dispose.Exception),
                HostDisposeState.NotInitialized => ShutdownFailureDetail.NotSupported(ShutdownFailurePhase.HostDispose),
                _ => ShutdownFailureDetail.Unknown(ShutdownFailurePhase.HostDispose, hostResult.Dispose.Exception),
            };
        }

        return ShutdownFailureDetail.Unknown(ShutdownFailurePhase.HostDispose);
    }

    private readonly record struct LifecycleStopOutcome(bool Success, ShutdownFailureDetail? Failure)
    {
        public static LifecycleStopOutcome Success() => new(true, null);

        public static LifecycleStopOutcome Timeout(Exception? exception = null) =>
            new(false, ShutdownFailureDetail.Timeout(ShutdownFailurePhase.LifecycleStop, exception));

        public static LifecycleStopOutcome Canceled() =>
            new(false, ShutdownFailureDetail.Canceled(ShutdownFailurePhase.LifecycleStop));

        public static LifecycleStopOutcome Failed(Exception exception) =>
            new(false, ShutdownFailureDetail.Error(ShutdownFailurePhase.LifecycleStop, exception));
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
