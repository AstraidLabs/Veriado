using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services.Shutdown;

public sealed class ShutdownOrchestrator : IShutdownOrchestrator, IAsyncDisposable
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    private readonly IConfirmService _confirmService;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly IHostShutdownService _hostShutdownService;
    private readonly ILogger<ShutdownOrchestrator> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool _completed;

    public ShutdownOrchestrator(
        IConfirmService confirmService,
        IHostApplicationLifetime applicationLifetime,
        IHostShutdownService hostShutdownService,
        ILogger<ShutdownOrchestrator> logger)
    {
        _confirmService = confirmService ?? throw new ArgumentNullException(nameof(confirmService));
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
            if (_completed)
            {
                _logger.LogDebug("Shutdown has already completed. Allowing close.");
                return ShutdownResult.Allow();
            }

            _logger.LogInformation("Shutdown requested with reason {Reason}.", reason);

            var confirmed = await ConfirmShutdownAsync(reason, cancellationToken).ConfigureAwait(true);
            if (!confirmed)
            {
                _logger.LogInformation("Shutdown canceled by user confirmation.");
                return ShutdownResult.Cancel();
            }

            ExecuteStopApplication();

            await StopHostAsync(cancellationToken).ConfigureAwait(false);
            await DisposeHostAsync().ConfigureAwait(false);

            _completed = true;
            _logger.LogInformation("Shutdown sequence finished.");
            return ShutdownResult.Allow();
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

    public ValueTask DisposeAsync()
    {
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<bool> ConfirmShutdownAsync(ShutdownReason reason, CancellationToken cancellationToken)
    {
        if (reason != ShutdownReason.AppWindowClosing)
        {
            return true;
        }

        return await _confirmService
            .TryConfirmAsync("Ukončit aplikaci?", "Opravdu si přejete ukončit aplikaci?", "Ukončit", "Zůstat", cancellationToken)
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

    private async Task StopHostAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stopCts.CancelAfter(StopTimeout);

            await _hostShutdownService.StopAsync(stopCts.Token).ConfigureAwait(false);
            _logger.LogDebug("Host stopped successfully.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Host stop operation timed out after {Timeout}.", StopTimeout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Host stop operation failed.");
        }
    }

    private async Task DisposeHostAsync()
    {
        try
        {
            await _hostShutdownService.DisposeAsync().ConfigureAwait(false);
            _logger.LogDebug("Host disposed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Host dispose operation failed.");
        }
    }
}
