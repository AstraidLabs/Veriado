using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veriado.Infrastructure.Diagnostics;
using Veriado.Infrastructure.Search;
using Veriado.Infrastructure.Lifecycle;

namespace Veriado.Infrastructure.Idempotency;

/// <summary>
/// Periodically removes expired idempotency keys from the backing store.
/// </summary>
internal sealed class IdempotencyCleanupWorker : BackgroundService
{
    private static readonly TimeSpan IterationTimeout = TimeSpan.FromMinutes(10);
    private const string MonitorServiceName = nameof(IdempotencyCleanupWorker);

    private readonly InfrastructureOptions _options;
    private readonly IClock _clock;
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<IdempotencyCleanupWorker> _logger;
    private readonly IAppLifecycleService _lifecycleService;
    private readonly IAppHealthMonitor _healthMonitor;

    public IdempotencyCleanupWorker(
        InfrastructureOptions options,
        IClock clock,
        ISqliteConnectionFactory connectionFactory,
        IAppLifecycleService lifecycleService,
        IAppHealthMonitor healthMonitor,
        ILogger<IdempotencyCleanupWorker> logger)
    {
        _options = options;
        _clock = clock;
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _lifecycleService = lifecycleService ?? throw new ArgumentNullException(nameof(lifecycleService));
        _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _healthMonitor.ReportBackgroundState(MonitorServiceName, BackgroundServiceRunState.Starting);

        if (_options.IdempotencyKeyTtl <= TimeSpan.Zero)
        {
            _logger.LogInformation("Idempotency cleanup worker disabled (TTL not configured)");
            _healthMonitor.ReportBackgroundState(MonitorServiceName, BackgroundServiceRunState.Stopped, "TTL not configured.");
            return;
        }

        var delay = _options.IdempotencyCleanupInterval <= TimeSpan.Zero
            ? TimeSpan.FromHours(1)
            : _options.IdempotencyCleanupInterval;

        _healthMonitor.ReportBackgroundState(MonitorServiceName, BackgroundServiceRunState.Running);

        while (!stoppingToken.IsCancellationRequested && !_lifecycleService.RunToken.IsCancellationRequested)
        {
            using var iterationCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _lifecycleService.RunToken);
            iterationCts.CancelAfter(IterationTimeout);
            var iterationToken = iterationCts.Token;

            try
            {
                var wasPaused = _lifecycleService.PauseToken.IsPaused;
                if (wasPaused)
                {
                    _healthMonitor.ReportBackgroundState(MonitorServiceName, BackgroundServiceRunState.Paused);
                    _logger.LogDebug("Idempotency cleanup worker paused by lifecycle.");
                }

                await _lifecycleService.PauseToken.WaitIfPausedAsync(iterationToken).ConfigureAwait(false);

                if (wasPaused)
                {
                    _healthMonitor.ReportBackgroundState(MonitorServiceName, BackgroundServiceRunState.Running);
                    _logger.LogDebug("Idempotency cleanup worker resumed after lifecycle pause.");
                }
            }
            catch (OperationCanceledException) when (iterationToken.IsCancellationRequested)
            {
                if (stoppingToken.IsCancellationRequested || _lifecycleService.RunToken.IsCancellationRequested)
                {
                    break;
                }

                continue;
            }

            try
            {
                var iterationWatch = Stopwatch.StartNew();
                await CleanupAsync(iterationToken).ConfigureAwait(false);
                iterationWatch.Stop();
                _logger.LogInformation(
                    "Idempotency cleanup iteration completed in {Duration}.",
                    iterationWatch.Elapsed);
                _healthMonitor.ReportBackgroundIteration(
                    MonitorServiceName,
                    BackgroundIterationOutcome.Success,
                    iterationWatch.Elapsed,
                    message: null);
            }
            catch (OperationCanceledException) when (iterationToken.IsCancellationRequested)
            {
                if (stoppingToken.IsCancellationRequested || _lifecycleService.RunToken.IsCancellationRequested)
                {
                    break;
                }

                if (iterationCts.IsCancellationRequested)
                {
                    _logger.LogWarning("Idempotency cleanup iteration timed out after {Timeout}.", IterationTimeout);
                    _healthMonitor.ReportBackgroundIteration(
                        MonitorServiceName,
                        BackgroundIterationOutcome.Timeout,
                        IterationTimeout,
                        message: "Cleanup iteration timed out.");
                }

                continue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean expired idempotency keys");
                _healthMonitor.ReportBackgroundIteration(
                    MonitorServiceName,
                    BackgroundIterationOutcome.Failed,
                    duration: null,
                    exception: ex,
                    message: ex.Message);
            }

            try
            {
                await PauseResponsiveDelay.DelayAsync(delay, _lifecycleService.PauseToken, iterationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (iterationToken.IsCancellationRequested)
            {
                if (stoppingToken.IsCancellationRequested || _lifecycleService.RunToken.IsCancellationRequested)
                {
                    break;
                }

                if (_lifecycleService.PauseToken.IsPaused)
                {
                    _healthMonitor.ReportBackgroundState(MonitorServiceName, BackgroundServiceRunState.Paused);
                }
            }
        }

        _healthMonitor.ReportBackgroundState(MonitorServiceName, BackgroundServiceRunState.Stopped);
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = lease.Connection;
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var existsCommand = connection.CreateCommand();
        existsCommand.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'idempotency_keys' LIMIT 1;";
        var tableExists = await existsCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (tableExists is null || tableExists == DBNull.Value)
        {
            _logger.LogDebug("Skipping idempotency cleanup because the idempotency_keys table does not exist yet");
            return;
        }

        var cutoff = _clock.UtcNow - _options.IdempotencyKeyTtl;
        await using var deleteCommand = connection.CreateCommand();
        deleteCommand.CommandText = "DELETE FROM idempotency_keys WHERE created_utc < $cutoff;";
        deleteCommand.Parameters.Add("$cutoff", SqliteType.Text).Value =
            cutoff.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        int removed;
        try
        {
            removed = await deleteCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex) when (ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(ex, "Skipping idempotency cleanup because the idempotency_keys table does not exist yet");
            return;
        }

        if (removed > 0)
        {
            _logger.LogInformation("Removed {Removed} expired idempotency keys", removed);
        }
    }
}
