using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
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

    private readonly IIndexAuditor _auditor;
    private readonly InfrastructureOptions _options;
    private readonly IAppLifecycleService _lifecycleService;
    private readonly ILogger<IndexAuditBackgroundService> _logger;
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
        ILogger<IndexAuditBackgroundService> logger)
    {
        _auditor = auditor ?? throw new ArgumentNullException(nameof(auditor));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _lifecycleService = lifecycleService ?? throw new ArgumentNullException(nameof(lifecycleService));
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

        try
        {
            await InitialJitterAsync(jitter, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("Index audit service stopping during initial jitter.");
            _logger.LogInformation(LogIds.ServiceStop, "Index audit service stopped.");
            return;
        }

        var consecutiveFailures = 0;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _lifecycleService.RunToken);
                var effectiveToken = combinedCts.Token;

                try
                {
                    await _lifecycleService.PauseToken.WaitIfPausedAsync(effectiveToken).ConfigureAwait(false);
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

                var result = await RunAuditIterationSafeAsync(iterationTimeout, effectiveToken, stoppingToken).ConfigureAwait(false);
                if (result == IterationResult.Shutdown)
                {
                    break;
                }

                consecutiveFailures = result switch
                {
                    IterationResult.Timeout or IterationResult.Failed => consecutiveFailures + 1,
                    _ => 0,
                };

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
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(LogIds.ServiceCrashed, ex, "Index audit service crashed. Stopping the service.");
        }
        finally
        {
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

    private async Task<IterationResult> RunAuditIterationSafeAsync(TimeSpan iterationTimeout, CancellationToken effectiveToken, CancellationToken stoppingToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(effectiveToken);
        cts.CancelAfter(iterationTimeout);

        try
        {
            _logger.LogDebug(LogIds.TickStart, "Running periodic FTS audit iteration…");
            var outcome = await RunAuditAsync(cts.Token).ConfigureAwait(false);

            return outcome;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested || _lifecycleService.RunToken.IsCancellationRequested)
        {
            return IterationResult.Shutdown;
        }
        catch (OperationCanceledException) when (effectiveToken.IsCancellationRequested)
        {
            _logger.LogDebug(LogIds.TickCanceled, "Index audit iteration canceled by lifecycle token.");
            return IterationResult.Shutdown;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(LogIds.TickTimeout, "Index audit iteration was canceled due to iteration timeout {Timeout}.", iterationTimeout);
            return IterationResult.Timeout;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(LogIds.TickFailed, ex, "Index audit iteration failed.");
            return IterationResult.Failed;
        }
    }

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
