using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
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
        ILogger<IndexAuditBackgroundService> logger)
    {
        _auditor = auditor ?? throw new ArgumentNullException(nameof(auditor));
        _options = options ?? throw new ArgumentNullException(nameof(options));
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

        _logger.LogInformation("Index audit service started. Interval={Interval}, IterationTimeout={IterationTimeout}, Jitter≤{Jitter}.",
            interval, iterationTimeout, jitter);

        // VŠE dovnitř try – ať neuteče cancel/ODE mimo ExecuteAsync
        using var timer = new PeriodicTimer(interval);

        // Volitelně: při zrušení tokenu rovnou dispose timeru → WaitForNextTickAsync skončí ODE
        using var _ = stoppingToken.Register(static state => ((PeriodicTimer)state!).Dispose(), timer);

        try
        {
            try
            {
                await InitialJitterAsync(jitter, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (stoppingToken.IsCancellationRequested)
                    _logger.LogDebug("Index audit service stopping during initial jitter.");
                return;
            }

            while (true)
            {
                bool tick;
                try
                {
                    // ř. 76 dřív padal – nově chytáme i ODE
                    tick = await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Graceful stop přes token
                    if (stoppingToken.IsCancellationRequested)
                        _logger.LogDebug("Index audit service stopping due to host cancellation.");
                    break;
                }
                catch (ObjectDisposedException)
                {
                    // Timer byl zrušen během awaitu (během shutdownu) – také graceful stop
                    _logger.LogDebug("Index audit service stopping because timer was disposed.");
                    break;
                }

                if (!tick) break; // timer ukončen

                await RunAuditIterationSafeAsync(iterationTimeout, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Index audit service crashed. Stopping the service.");
        }
        finally
        {
            _logger.LogInformation("Index audit service stopped.");
        }
    }

    private async Task InitialJitterAsync(TimeSpan jitter, CancellationToken ct)
    {
        if (jitter <= TimeSpan.Zero)
            return;

        var delay = RandomUpTo(jitter);
        if (delay > TimeSpan.Zero)
        {
            await DelayWithCancellationAsync(delay, ct).ConfigureAwait(false);
        }
    }

    private static async Task DelayWithCancellationAsync(TimeSpan delay, CancellationToken ct)
    {
        if (!ct.CanBeCanceled)
        {
            await Task.Delay(delay).ConfigureAwait(false);
            return;
        }

        if (ct.IsCancellationRequested)
            return;

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var registration = ct.Register(static state =>
        {
            var tcs = (TaskCompletionSource<object?>)state!;
            tcs.TrySetResult(null);
        }, completion);

        await Task.WhenAny(Task.Delay(delay), completion.Task).ConfigureAwait(false);
    }

    private enum IterationResult { Success, NoIssues, Scheduled, Timeout, Failed, Shutdown }

    private async Task<IterationResult> RunAuditIterationSafeAsync(TimeSpan iterationTimeout, CancellationToken stoppingToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        cts.CancelAfter(iterationTimeout);

        try
        {
            _logger.LogDebug(LogIds.TickStart, "Running periodic FTS audit iteration…");
            var outcome = await RunAuditAsync(cts.Token).ConfigureAwait(false);

            return outcome;
        }
        catch (OperationCanceledException oce) when (stoppingToken.IsCancellationRequested || oce.CancellationToken == stoppingToken)
        {
            // čistý shutdown – necháme nadřazenou smyčku doběhnout
            return IterationResult.Shutdown;
        }
        catch (OperationCanceledException)
        {
            // vypršel per-iterace timeout
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

    private static TimeSpan ComputeBackoffDelay(int consecutiveFailures, TimeSpan jitter)
    {
        // Backoff jen při chybě/timeoutu; úspěch resetuje čítač.
        if (consecutiveFailures <= 0)
            return RandomUpTo(jitter);

        var exp = Math.Min(consecutiveFailures, MaxBackoffExponent);
        // základ 5s * 2^n, cap na 2 min
        var baseDelay = TimeSpan.FromSeconds(5 * Math.Pow(2, exp));
        if (baseDelay > TimeSpan.FromMinutes(2))
            baseDelay = TimeSpan.FromMinutes(2);

        return baseDelay + RandomUpTo(jitter);
    }

    private static TimeSpan RandomUpTo(TimeSpan max)
    {
        if (max <= TimeSpan.Zero) return TimeSpan.Zero;
        // Thread-static Random by šel taky; tady máme instanční _rng.
        var rng = new Random();
        var ms = rng.NextInt64(0, (long)max.TotalMilliseconds + 1);
        return TimeSpan.FromMilliseconds(ms);
    }
}
