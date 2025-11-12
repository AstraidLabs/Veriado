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
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TaskCompletionSource<object?> _stopCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task<HostShutdownResult>? _shutdownTask;
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

        _shutdownTask = null;
        _stopCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public Task WhenStopped => _stopCompletion.Task;

    public async Task<HostShutdownResult> StopAndDisposeAsync(
        TimeSpan stopTimeout,
        TimeSpan disposeTimeout,
        CancellationToken cancellationToken)
    {
        ValidateTimeout(stopTimeout, nameof(stopTimeout));
        ValidateTimeout(disposeTimeout, nameof(disposeTimeout));

        Task<HostShutdownResult> shutdownOperation;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_shutdownTask is null)
            {
                var host = Interlocked.Exchange(ref _host, null);
                if (host is null)
                {
                    _logger.LogDebug("Host shutdown requested but host is not initialized.");
                    var result = HostShutdownResult.NotInitialized();
                    shutdownOperation = Task.FromResult(result);
                    _shutdownTask = shutdownOperation;
                    _stopCompletion.TrySetResult(null);
                }
                else
                {
                    _logger.LogInformation(
                        "Coordinating host shutdown (stop timeout: {StopTimeout}, dispose timeout: {DisposeTimeout}).",
                        stopTimeout,
                        disposeTimeout);

                    shutdownOperation = ShutdownCoreAsync(host, stopTimeout, disposeTimeout);
                    _shutdownTask = shutdownOperation;
                }
            }
            else
            {
                shutdownOperation = _shutdownTask;
            }
        }
        finally
        {
            _gate.Release();
        }

        return await WaitForShutdownAsync(shutdownOperation, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HostShutdownResult> ShutdownCoreAsync(IHost host, TimeSpan stopTimeout, TimeSpan disposeTimeout)
    {
        try
        {
            var stopResult = await StopHostAsync(host, stopTimeout).ConfigureAwait(false);
            var disposeResult = await DisposeHostAsync(host, disposeTimeout).ConfigureAwait(false);

            if (stopResult.IsSuccess && disposeResult.IsSuccess)
            {
                _logger.LogInformation("Host shutdown completed successfully.");
            }
            else
            {
                _logger.LogWarning(
                    "Host shutdown completed with Stop={StopState} and Dispose={DisposeState}.",
                    stopResult.State,
                    disposeResult.State);
            }

            return new HostShutdownResult(stopResult, disposeResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected failure during host shutdown.");
            return new HostShutdownResult(HostStopResult.Failed(ex), HostDisposeResult.Failed(ex));
        }
        finally
        {
            _stopCompletion.TrySetResult(null);
        }
    }

    private async Task<HostStopResult> StopHostAsync(IHost host, TimeSpan timeout)
    {
        using var stopCts = CreateTimeoutSource(timeout);
        var token = stopCts.Token;

        try
        {
            await host.StopAsync(token).ConfigureAwait(false);
            _logger.LogInformation("Host stopped successfully.");
            return HostStopResult.Completed();
        }
        catch (OperationCanceledException ex) when (token.IsCancellationRequested)
        {
            if (timeout > TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            {
                _logger.LogWarning(ex, "Host stop timed out after {Timeout}.", timeout);
                return HostStopResult.TimedOut(ex);
            }

            _logger.LogInformation("Host stop canceled via cancellation token.");
            return HostStopResult.Canceled(ex);
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Host stop skipped because the host was already disposed.");
            return HostStopResult.AlreadyStopped(ex);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "Host stop skipped because the host was not initialized.");
            return HostStopResult.NotInitialized(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Host stop failed.");
            return HostStopResult.Failed(ex);
        }
    }

    private async Task<HostDisposeResult> DisposeHostAsync(IHost host, TimeSpan timeout)
    {
        try
        {
            Task disposeTask = host is IAsyncDisposable asyncDisposable
                ? asyncDisposable.DisposeAsync().AsTask()
                : Task.Run(host.Dispose);

            await WaitWithTimeoutAsync(disposeTask, timeout).ConfigureAwait(false);

            _logger.LogInformation("Host disposed successfully.");
            return HostDisposeResult.Completed();
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "Host dispose timed out after {Timeout}.", timeout);
            return HostDisposeResult.Failed(ex);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogInformation(ex, "Host dispose canceled via cancellation token.");
            return HostDisposeResult.AlreadyDisposed(ex);
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Host dispose skipped because the host was already disposed.");
            return HostDisposeResult.AlreadyDisposed(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Host dispose failed.");
            return HostDisposeResult.Failed(ex);
        }
    }

    private async Task<HostShutdownResult> WaitForShutdownAsync(Task<HostShutdownResult> operation, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            return await operation.ConfigureAwait(false);
        }

        try
        {
            return await operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Host shutdown wait canceled via caller token.");
            throw;
        }
    }

    private static CancellationTokenSource CreateTimeoutSource(TimeSpan timeout)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return new CancellationTokenSource();
        }

        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var source = new CancellationTokenSource();
        if (timeout > TimeSpan.Zero)
        {
            source.CancelAfter(timeout);
        }

        return source;
    }

    private static async Task WaitWithTimeoutAsync(Task task, TimeSpan timeout)
    {
        if (timeout == Timeout.InfiniteTimeSpan || timeout <= TimeSpan.Zero)
        {
            await task.ConfigureAwait(false);
            return;
        }

        await task.WaitAsync(timeout).ConfigureAwait(false);
    }

    private static void ValidateTimeout(TimeSpan timeout, string parameterName)
    {
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
