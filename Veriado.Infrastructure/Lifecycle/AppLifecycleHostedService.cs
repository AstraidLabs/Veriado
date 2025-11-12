using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Veriado.Infrastructure.Lifecycle;

public sealed class AppLifecycleHostedService : IHostedService, IAppLifecycleService, IAsyncDisposable
{
    private static readonly TimeSpan DefaultStartTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DefaultPauseTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultResumeTimeout = TimeSpan.FromSeconds(10);

    private readonly ILogger<AppLifecycleHostedService> _logger;
    private readonly PauseTokenSource _pauseTokenSource;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AppLifecycleState _state = AppLifecycleState.Stopped;
    private CancellationTokenSource? _runCts;
    private bool _disposed;

    public AppLifecycleHostedService(ILogger<AppLifecycleHostedService> logger, PauseTokenSource pauseTokenSource)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pauseTokenSource = pauseTokenSource ?? throw new ArgumentNullException(nameof(pauseTokenSource));
    }

    public AppLifecycleState State => _state;

    public CancellationToken RunToken => _runCts?.Token ?? CancellationToken.None;

    public PauseToken PauseToken => _pauseTokenSource.Token;

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

    public async Task PauseAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (_state is AppLifecycleState.Paused or AppLifecycleState.Pausing)
            {
                _logger.LogDebug("Pause requested while already paused.");
                return;
            }

            if (_state is not AppLifecycleState.Running)
            {
                _logger.LogWarning("Pause requested while lifecycle state is {State}. Ignoring.", _state);
                return;
            }

            await PauseInternalAsync(ct).ConfigureAwait(false);
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
            _state = AppLifecycleState.Disposed;
            _pauseTokenSource.TryResume();
            _runCts?.Cancel();
            _runCts?.Dispose();
            _runCts = null;
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

        _state = AppLifecycleState.Starting;
        _logger.LogInformation("Lifecycle starting.");

        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token);

        try
        {
            await Task.CompletedTask.ConfigureAwait(false);
            _state = AppLifecycleState.Running;
            _logger.LogInformation("Lifecycle running.");
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _state = AppLifecycleState.Faulted;
            _logger.LogWarning("Lifecycle start timed out after {Timeout}.", DefaultStartTimeout);
            throw;
        }
        catch (Exception ex)
        {
            _state = AppLifecycleState.Faulted;
            _logger.LogError(ex, "Lifecycle start failed.");
            throw;
        }
    }

    private async Task StopInternalAsync(CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(DefaultStopTimeout);

        _state = AppLifecycleState.Stopping;
        _logger.LogInformation("Lifecycle stopping.");

        _pauseTokenSource.TryResume();
        _runCts?.Cancel();

        try
        {
            await Task.CompletedTask.ConfigureAwait(false);
            _state = AppLifecycleState.Stopped;
            _logger.LogInformation("Lifecycle stopped.");
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _state = AppLifecycleState.Faulted;
            _logger.LogWarning("Lifecycle stop timed out after {Timeout}.", DefaultStopTimeout);
            throw;
        }
        finally
        {
            _runCts?.Dispose();
            _runCts = null;
        }
    }

    private async Task PauseInternalAsync(CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(DefaultPauseTimeout);

        _state = AppLifecycleState.Pausing;
        _logger.LogInformation("Lifecycle pausing.");

        try
        {
            var paused = _pauseTokenSource.TryPause();
            if (!paused)
            {
                _logger.LogDebug("Lifecycle was already paused.");
            }

            await Task.CompletedTask.ConfigureAwait(false);
            _state = AppLifecycleState.Paused;
            _logger.LogInformation("Lifecycle paused.");
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _state = AppLifecycleState.Faulted;
            _logger.LogWarning("Lifecycle pause timed out after {Timeout}.", DefaultPauseTimeout);
            throw;
        }
    }

    private async Task ResumeInternalAsync(CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(DefaultResumeTimeout);

        _state = AppLifecycleState.Resuming;
        _logger.LogInformation("Lifecycle resuming.");

        try
        {
            var resumed = _pauseTokenSource.TryResume();
            if (!resumed)
            {
                _logger.LogDebug("Lifecycle was not paused.");
            }

            await Task.CompletedTask.ConfigureAwait(false);
            _state = AppLifecycleState.Running;
            _logger.LogInformation("Lifecycle resumed.");
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _state = AppLifecycleState.Faulted;
            _logger.LogWarning("Lifecycle resume timed out after {Timeout}.", DefaultResumeTimeout);
            throw;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AppLifecycleHostedService));
        }
    }
}
