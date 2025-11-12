using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veriado.Infrastructure.Diagnostics;

namespace Veriado.Infrastructure.Lifecycle;

public sealed class AppLifecycleHostedService : IHostedService, IAppLifecycleService, IAsyncDisposable
{
    private static readonly TimeSpan DefaultStartTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan DefaultPauseTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan DefaultResumeTimeout = TimeSpan.FromSeconds(45);

    private readonly ILogger<AppLifecycleHostedService> _logger;
    private readonly PauseTokenSource _pauseTokenSource;
    private readonly IAppHealthMonitor? _healthMonitor;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AppLifecycleState _state = AppLifecycleState.Stopped;
    private CancellationTokenSource? _runCts;
    private bool _disposed;

    public AppLifecycleHostedService(
        ILogger<AppLifecycleHostedService> logger,
        PauseTokenSource pauseTokenSource,
        IAppHealthMonitor? healthMonitor = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pauseTokenSource = pauseTokenSource ?? throw new ArgumentNullException(nameof(pauseTokenSource));
        _healthMonitor = healthMonitor;
    }

    public AppLifecycleState State => _state;

    public CancellationToken RunToken => _runCts?.Token ?? CancellationToken.None;

    public PauseToken PauseToken => _pauseTokenSource.Token;

    public event Func<CancellationToken, Task>? Starting;

    public event Func<CancellationToken, Task>? Stopping;

    public event Func<CancellationToken, Task>? Paused;

    public event Func<CancellationToken, Task>? Resumed;

    Task IHostedService.StartAsync(CancellationToken cancellationToken) => StartAsync(cancellationToken);

    Task IHostedService.StopAsync(CancellationToken cancellationToken) => StopAsync(cancellationToken);

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            switch (_state)
            {
                case AppLifecycleState.Running:
                case AppLifecycleState.Starting:
                case AppLifecycleState.Resuming:
                    _logger.LogDebug("Start requested while already running. Ignoring.");
                    return;
                case AppLifecycleState.Paused:
                    _logger.LogInformation("Start requested while paused. Resuming instead.");
                    await ResumeInternalAsync(ct).ConfigureAwait(false);
                    return;
                case AppLifecycleState.Disposed:
                    throw new ObjectDisposedException(nameof(AppLifecycleHostedService));
            }

            await StartInternalAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (_state is AppLifecycleState.Stopped or AppLifecycleState.Stopping)
            {
                _logger.LogDebug("Stop requested while already stopped.");
                return;
            }

            await StopInternalAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PauseResult> PauseAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (_state is AppLifecycleState.Paused or AppLifecycleState.Pausing)
            {
                _logger.LogDebug("Pause requested while already paused.");
                return PauseResult.Success(TimeSpan.Zero);
            }

            if (_state is not AppLifecycleState.Running)
            {
                _logger.LogWarning("Pause requested while lifecycle state is {State}. Ignoring.", _state);
                return PauseResult.NotSupported();
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                await PauseInternalAsync(ct).ConfigureAwait(false);
                stopwatch.Stop();
                return PauseResult.Success(stopwatch.Elapsed);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                stopwatch.Stop();
                _logger.LogDebug(ex, "Lifecycle pause canceled or timed out.");
                return PauseResult.RetryableFailure(ex);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogWarning(ex, "Lifecycle pause failed.");
                return PauseResult.RetryableFailure(ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResumeAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (_state is AppLifecycleState.Running or AppLifecycleState.Resuming)
            {
                _logger.LogDebug("Resume requested while already running.");
                return;
            }

            if (_state is not AppLifecycleState.Paused)
            {
                _logger.LogWarning("Resume requested while lifecycle state is {State}. Ignoring.", _state);
                return;
            }

            await ResumeInternalAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RestartAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            _logger.LogInformation("Restart requested.");
            TransitionTo(AppLifecycleState.Restarting);
            await StopInternalAsync(ct).ConfigureAwait(false);
            await StartInternalAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                if (_state is not AppLifecycleState.Stopped and not AppLifecycleState.Disposed)
                {
                    await StopInternalAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lifecycle encountered an error during disposal stop phase.");
            }

            TransitionTo(AppLifecycleState.Disposed);
            _pauseTokenSource.TryResume();
            _runCts?.Cancel();
            _runCts?.Dispose();
            _runCts = null;
            _logger.LogInformation("Lifecycle disposed.");
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task StartInternalAsync(CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(DefaultStartTimeout);

        TransitionTo(AppLifecycleState.Starting);
        _logger.LogInformation("Lifecycle starting.");

        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await InvokeHandlersAsync(Starting, DefaultStartTimeout, timeoutCts.Token, "Starting").ConfigureAwait(false);
            TransitionTo(AppLifecycleState.Running);
            stopwatch.Stop();
            _logger.LogInformation("Lifecycle running after {Duration}.", stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            TransitionTo(AppLifecycleState.Faulted);
            stopwatch.Stop();
            _logger.LogWarning("Lifecycle start timed out after {Timeout}.", DefaultStartTimeout);
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            TransitionTo(AppLifecycleState.Stopped);
            stopwatch.Stop();
            _logger.LogDebug("Lifecycle start canceled by caller.");
            _runCts?.Dispose();
            _runCts = null;
            throw;
        }
        catch (Exception ex)
        {
            TransitionTo(AppLifecycleState.Faulted);
            stopwatch.Stop();
            _logger.LogError(ex, "Lifecycle start failed.");
            throw;
        }
    }

    private async Task StopInternalAsync(CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(DefaultStopTimeout);

        TransitionTo(AppLifecycleState.Stopping);
        _logger.LogInformation("Lifecycle stopping.");

        _pauseTokenSource.TryResume();
        _runCts?.Cancel();

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await InvokeHandlersAsync(Stopping, DefaultStopTimeout, timeoutCts.Token, "Stopping").ConfigureAwait(false);
            TransitionTo(AppLifecycleState.Stopped);
            stopwatch.Stop();
            _logger.LogInformation("Lifecycle stopped after {Duration}.", stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            TransitionTo(AppLifecycleState.Faulted);
            stopwatch.Stop();
            _logger.LogWarning("Lifecycle stop timed out after {Timeout}.", DefaultStopTimeout);
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            TransitionTo(AppLifecycleState.Running);
            stopwatch.Stop();
            _logger.LogDebug("Lifecycle stop canceled by caller.");
            _runCts?.Dispose();
            _runCts = new CancellationTokenSource();
            throw;
        }
        catch (Exception ex)
        {
            TransitionTo(AppLifecycleState.Faulted);
            stopwatch.Stop();
            _logger.LogError(ex, "Lifecycle stop failed.");
            throw;
        }
        finally
        {
            if (_state is AppLifecycleState.Stopped or AppLifecycleState.Faulted or AppLifecycleState.Disposed)
            {
                _runCts?.Dispose();
                _runCts = null;
            }
        }
    }

    private async Task PauseInternalAsync(CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(DefaultPauseTimeout);

        TransitionTo(AppLifecycleState.Pausing);
        _logger.LogInformation("Lifecycle pausing.");

        try
        {
            var paused = _pauseTokenSource.TryPause();
            if (!paused)
            {
                _logger.LogDebug("Lifecycle was already paused.");
            }

            TransitionTo(AppLifecycleState.Paused);
            var stopwatch = Stopwatch.StartNew();
            await InvokeHandlersAsync(Paused, DefaultPauseTimeout, timeoutCts.Token, "Paused").ConfigureAwait(false);
            stopwatch.Stop();
            _logger.LogInformation("Lifecycle paused (handlers completed in {Duration}).", stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _pauseTokenSource.TryResume();
            TransitionTo(AppLifecycleState.Running);
            _logger.LogDebug("Lifecycle pause canceled by caller.");
            throw;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            TransitionTo(AppLifecycleState.Faulted);
            _logger.LogWarning("Lifecycle pause timed out after {Timeout}.", DefaultPauseTimeout);
            throw;
        }
    }

    private async Task ResumeInternalAsync(CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(DefaultResumeTimeout);

        TransitionTo(AppLifecycleState.Resuming);
        _logger.LogInformation("Lifecycle resuming.");

        try
        {
            var resumed = _pauseTokenSource.TryResume();
            if (!resumed)
            {
                _logger.LogDebug("Lifecycle was not paused.");
            }

            TransitionTo(AppLifecycleState.Running);
            var stopwatch = Stopwatch.StartNew();
            await InvokeHandlersAsync(Resumed, DefaultResumeTimeout, timeoutCts.Token, "Resumed").ConfigureAwait(false);
            stopwatch.Stop();
            _logger.LogInformation("Lifecycle resumed (handlers completed in {Duration}).", stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _pauseTokenSource.TryPause();
            TransitionTo(AppLifecycleState.Paused);
            _logger.LogDebug("Lifecycle resume canceled by caller.");
            throw;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            TransitionTo(AppLifecycleState.Faulted);
            _logger.LogWarning("Lifecycle resume timed out after {Timeout}.", DefaultResumeTimeout);
            throw;
        }
    }

    private void TransitionTo(AppLifecycleState newState)
    {
        _state = newState;
        _healthMonitor?.ReportLifecycleState(newState);
    }

    private async Task InvokeHandlersAsync(
        Func<CancellationToken, Task>? handlers,
        TimeSpan timeout,
        CancellationToken externalToken,
        string stage)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList())
        {
            var callback = (Func<CancellationToken, Task>)handler;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            if (timeout > TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            {
                timeoutCts.CancelAfter(timeout);
            }

            try
            {
                var sw = Stopwatch.StartNew();
                await callback(timeoutCts.Token).ConfigureAwait(false);
                sw.Stop();
                _logger.LogDebug(
                    "Lifecycle {Stage} handler {Handler} completed in {Duration}.",
                    stage,
                    DescribeHandler(callback),
                    sw.Elapsed);
            }
            catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested && !externalToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "Lifecycle {Stage} handler {Handler} timed out after {Timeout}.",
                    stage,
                    DescribeHandler(callback),
                    timeout);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Lifecycle {Stage} handler {Handler} failed.",
                    stage,
                    DescribeHandler(callback));
            }
        }
    }

    private static string DescribeHandler(Delegate handler)
    {
        var method = handler.Method;
        var typeName = method.DeclaringType?.Name ?? "<unknown>";
        return $"{typeName}.{method.Name}";
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AppLifecycleHostedService));
        }
    }
}
