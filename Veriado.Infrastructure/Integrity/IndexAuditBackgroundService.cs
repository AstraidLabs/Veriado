using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Infrastructure.Diagnostics;
using Veriado.Infrastructure.Lifecycle;
using Veriado.Infrastructure.Persistence.Options;

namespace Veriado.Infrastructure.Integrity;

/// <summary>
/// Periodicky spouští <see cref="IIndexAuditor"/> a detekuje/napravuje drift FTS indexů.
/// Odolné proti shutdownu, time-outům, a jednorázovým chybám (backoff + jitter).
/// </summary>
internal sealed class IndexAuditBackgroundService : BackgroundService
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(4);
    private static readonly TimeSpan DefaultIterationTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultJitter = TimeSpan.FromMinutes(3);
    private const int MaxBackoffExponent = 5; // 2^5 = 32×

    private const string MonitorServiceName = nameof(IndexAuditBackgroundService);

    private readonly IIndexAuditor _auditor;
    private readonly InfrastructureOptions _options;
    private readonly IAppLifecycleService _lifecycleService;
    private readonly ILogger<IndexAuditBackgroundService> _logger;
    private readonly IAppHealthMonitor _healthMonitor;
    private readonly Random _rng = new();

    private static class LogIds
    {
        public static readonly EventId ServiceStart = new(1000, nameof(ServiceStart));
        public static readonly EventId ServiceStop = new(1001, nameof(ServiceStop));
        public static readonly EventId TickStart = new(1010, nameof(TickStart));
        public static readonly EventId TickOk = new(1011, nameof(TickOk));
        public static readonly EventId TickNoIssues = new(1012, nameof(TickNoIssues));
        public static readonly EventId TickScheduled = new(1013, nameof(TickScheduled));
        public static readonly EventId TickCanceled = new(1020, nameof(TickCanceled));
        public static readonly EventId TickTimeout = new(1021, nameof(TickTimeout));
        public static readonly EventId TickFailed = new(1022, nameof(TickFailed));
        public static readonly EventId ServiceCrashed = new(1099, nameof(ServiceCrashed));
    }

    public IndexAuditBackgroundService(
        IIndexAuditor auditor,
        InfrastructureOptions options,
        IAppLifecycleService lifecycleService,
        IAppHealthMonitor healthMonitor,
        ILogger<IndexAuditBackgroundService> logger)
    {
        _auditor = auditor ?? throw new ArgumentNullException(nameof(auditor));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _lifecycleService = lifecycleService ?? throw new ArgumentNullException(nameof(lifecycleService));
        _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = NormalizeInterval(_options.IndexAuditInterval);
        var iterationTimeout = _options.IndexAuditIterationTimeout > TimeSpan.Zero
            ? _options.IndexAuditIterationTimeout
            : DefaultIterationTimeout;

        var jitter = _options.IndexAuditJitter > TimeSpan.Zero
            ? _options.IndexAuditJitter
            : DefaultJitter;

        _logger.LogInformation(LogIds.ServiceStart,
            "Index audit service started. Interval={Interval}, IterationTimeout={IterationTimeout}, Jitter≤{Jitter}.",
            interval,
            iterationTimeout,
            jitter);

        _healthMonitor.ReportBackgroundState(MonitorServiceName, BackgroundServiceRunState.Starting);

        try
        {
            await InitialJitterAsync(jitter, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("Index audit service stopping during initial jitter.");
            _healthMonitor.ReportBackgroundState(MonitorServiceName, BackgroundServiceRunState.Stopped);
            _logger.LogInformation(LogIds.ServiceStop, "Index audit service stopped.");
            return;
        }

        var consecutiveFailures = 0;

        _healthMonitor.ReportBackgroundState(MonitorServiceName, BackgroundServiceRunState.Running);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _lifecycleService.RunToken);
                var effectiveToken = combinedCts.Token;

                try
                {
                    var wasPaused = _lifecycleService.PauseToken.IsPaused;
                    if (wasPaused)
                    {
                        _healthMonitor.ReportBackgroundState(MonitorServiceName, BackgroundServiceRunState.Paused);
                        _logger.LogInformation("Index audit service paused by lifecycle.");
                    }

                    await _lifecycleService.PauseToken.WaitIfPausedAsync(effectiveToken).ConfigureAwait(false);

                    if (wasPaused)
                    {
                        _healthMonitor.ReportBackgroundState(MonitorServiceName, BackgroundServiceRunState.Running);
                        _logger.LogInformation("Index audit service resumed after lifecycle pause.");
                    }
                }
                catch (OperationCanceledException) when (effectiveToken.IsCancellationRequested)
                {
                    if (stoppingToken.IsCancellationRequested || _lifecycleService.RunToken.IsCancellationRequested)
                    {
                        _logger.LogDebug(LogIds.TickCanceled, "Index audit loop canceled due to shutdown.");
                        break;
                    }

                    continue;
                }

                var iterationWatch = Stopwatch.StartNew();
                var (result, exception) = await RunAuditIterationSafeAsync(iterationTimeout, effectiveToken, stoppingToken).ConfigureAwait(false);
                iterationWatch.Stop();

                if (result == IterationResult.Shutdown)
                {
                    break;
                }

                consecutiveFailures = result switch
                {
                    IterationResult.Timeout or IterationResult.Failed => consecutiveFailures + 1,
                    _ => 0,
                };

                var outcome = MapOutcome(result);
                var message = DescribeOutcome(result);
                _healthMonitor.ReportBackgroundIteration(MonitorServiceName, outcome, iterationWatch.Elapsed, exception, message);

                _logger.LogInformation(
                    LogIds.TickOk,
                    "Index audit iteration completed in {Duration} with outcome {Outcome}.",
                    iterationWatch.Elapsed,
                    outcome);

                var backoffDelay = ComputeBackoffDelay(consecutiveFailures, jitter);
                var delay = interval + backoffDelay;
                if (delay <= TimeSpan.Zero)
                {
                    continue;
                }

                try
                {
                    await PauseResponsiveDelay.DelayAsync(delay, _lifecycleService.PauseToken, effectiveToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (effectiveToken.IsCancellationRequested)
                {
                    if (stoppingToken.IsCancellationRequested || _lifecycleService.RunToken.IsCancellationRequested)
                    {
                        _logger.LogDebug(LogIds.TickCanceled, "Index audit delay canceled due to shutdown.");
                        break;
                    }

                    if (_lifecycleService.PauseToken.IsPaused)
                    {
                        _healthMonitor.ReportBackgroundState(MonitorServiceName, BackgroundServiceRunState.Paused);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(LogIds.ServiceCrashed, ex, "Index audit service crashed. Stopping the service.");
            _healthMonitor.ReportBackgroundState(MonitorServiceName, BackgroundServiceRunState.Faulted, ex.Message);
        }
        finally
        {
            _healthMonitor.ReportBackgroundState(MonitorServiceName, BackgroundServiceRunState.Stopped);
            _logger.LogInformation(LogIds.ServiceStop, "Index audit service stopped.");
        }
    }

    private async Task InitialJitterAsync(TimeSpan jitter, CancellationToken ct)
    {
        if (jitter <= TimeSpan.Zero)
        {
            return;
        }

        var delay = RandomUpTo(jitter);
        if (delay > TimeSpan.Zero)
        {
            await PauseResponsiveDelay.DelayAsync(delay, _lifecycleService.PauseToken, ct).ConfigureAwait(false);
        }
    }

    private enum IterationResult { Success, NoIssues, Scheduled, Timeout, Failed, Shutdown }

    private async Task<(IterationResult Result, Exception? Exception)> RunAuditIterationSafeAsync(TimeSpan iterationTimeout, CancellationToken effectiveToken, CancellationToken stoppingToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(effectiveToken);
        cts.CancelAfter(iterationTimeout);

        try
        {
            _logger.LogDebug(LogIds.TickStart, "Running periodic FTS audit iteration…");
            var outcome = await RunAuditAsync(cts.Token).ConfigureAwait(false);

            return (outcome, null);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested || _lifecycleService.RunToken.IsCancellationRequested)
        {
            return (IterationResult.Shutdown, null);
        }
        catch (OperationCanceledException) when (effectiveToken.IsCancellationRequested)
        {
            _logger.LogDebug(LogIds.TickCanceled, "Index audit iteration canceled by lifecycle token.");
            return (IterationResult.Shutdown, null);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(LogIds.TickTimeout, "Index audit iteration was canceled due to iteration timeout {Timeout}.", iterationTimeout);
            return (IterationResult.Timeout, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(LogIds.TickFailed, ex, "Index audit iteration failed.");
            return (IterationResult.Failed, ex);
        }
    }

    private static BackgroundIterationOutcome MapOutcome(IterationResult result) => result switch
    {
        IterationResult.Success => BackgroundIterationOutcome.Success,
        IterationResult.NoIssues => BackgroundIterationOutcome.Success,
        IterationResult.Scheduled => BackgroundIterationOutcome.NoWork,
        IterationResult.Timeout => BackgroundIterationOutcome.Timeout,
        IterationResult.Failed => BackgroundIterationOutcome.Failed,
        IterationResult.Shutdown => BackgroundIterationOutcome.Canceled,
        _ => BackgroundIterationOutcome.None,
    };

    private static string? DescribeOutcome(IterationResult result) => result switch
    {
        IterationResult.NoIssues => "No inconsistencies detected.",
        IterationResult.Scheduled => "Indexing scheduled for outstanding files.",
        IterationResult.Timeout => "Iteration timed out before completion.",
        IterationResult.Failed => "Iteration failed. See logs for details.",
        _ => null,
    };

    /// <summary>
    /// Vlastní práce auditu; vrací detailnější výsledek pro lepší řízení backoffu/logování.
    /// </summary>
    private async Task<IterationResult> RunAuditAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var summary = await _auditor.VerifyAsync(ct).ConfigureAwait(false);
        var issues = (summary.Missing?.Count ?? 0) + (summary.Drift?.Count ?? 0) + (summary.Extra?.Count ?? 0);

        if (issues == 0)
        {
            _logger.LogDebug(LogIds.TickNoIssues, "FTS audit completed: no discrepancies.");
            return IterationResult.NoIssues;
        }

        var scheduled = await _auditor.RepairDriftAsync(summary, ct).ConfigureAwait(false);

        if (scheduled > 0)
        {
            _logger.LogInformation(LogIds.TickScheduled,
                "FTS audit detected {IssueCount} discrepancies; scheduled {ScheduledCount} reindex operations.",
                issues, scheduled);
            return IterationResult.Scheduled;
        }

        _logger.LogInformation(LogIds.TickOk,
            "FTS audit detected {IssueCount} discrepancies; automated reindex is disabled in this phase.",
            issues);
        return IterationResult.Success;
    }

    private static TimeSpan NormalizeInterval(TimeSpan configured)
    {
        if (configured <= TimeSpan.Zero)
            return DefaultInterval;

        if (configured < MinimumInterval)
            return MinimumInterval;

        return configured;
    }

    private TimeSpan ComputeBackoffDelay(int consecutiveFailures, TimeSpan jitter)
    {
        if (consecutiveFailures <= 0)
        {
            return RandomUpTo(jitter);
        }

        var exp = Math.Min(consecutiveFailures, MaxBackoffExponent);
        var baseDelay = TimeSpan.FromSeconds(5 * Math.Pow(2, exp));
        if (baseDelay > TimeSpan.FromMinutes(2))
        {
            baseDelay = TimeSpan.FromMinutes(2);
        }

        return baseDelay + RandomUpTo(jitter);
    }

    private TimeSpan RandomUpTo(TimeSpan max)
    {
        if (max <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        lock (_rng)
        {
            var ms = _rng.NextDouble() * max.TotalMilliseconds;
            return TimeSpan.FromMilliseconds(ms);
        }
    }
}
