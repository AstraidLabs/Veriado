using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veriado.Appl.Abstractions;
using Veriado.Infrastructure.Diagnostics;
using Veriado.Infrastructure.Lifecycle;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.EventLog;
using Veriado.Infrastructure.Persistence.Options;

namespace Veriado.Infrastructure.Search;

internal sealed class ReindexQueueProcessorService : BackgroundService
{
    private const string MonitorServiceName = nameof(ReindexQueueProcessorService);
    private static readonly TimeSpan MinimumPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromMinutes(5);

    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IAppLifecycleService _lifecycleService;
    private readonly ISearchIndexCoordinator _coordinator;
    private readonly InfrastructureOptions _options;
    private readonly IAppHealthMonitor _healthMonitor;
    private readonly ILogger<ReindexQueueProcessorService> _logger;
    private readonly Random _random = new();

    public ReindexQueueProcessorService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IAppLifecycleService lifecycleService,
        ISearchIndexCoordinator coordinator,
        InfrastructureOptions options,
        IAppHealthMonitor healthMonitor,
        ILogger<ReindexQueueProcessorService> logger)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _lifecycleService = lifecycleService ?? throw new ArgumentNullException(nameof(lifecycleService));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = _options.ReindexQueuePollInterval > TimeSpan.Zero
            ? (_options.ReindexQueuePollInterval < MinimumPollInterval ? MinimumPollInterval : _options.ReindexQueuePollInterval)
            : TimeSpan.FromSeconds(15);
        var iterationTimeout = _options.ReindexQueueIterationTimeout > TimeSpan.Zero
            ? _options.ReindexQueueIterationTimeout
            : TimeSpan.FromMinutes(2);
        var baseErrorBackoff = _options.ReindexQueueErrorBackoff > TimeSpan.Zero
            ? _options.ReindexQueueErrorBackoff
            : TimeSpan.FromSeconds(30);
        var batchSize = Math.Max(1, _options.ReindexQueueBatchSize);

        _logger.LogInformation(
            "Reindex queue processor started. BatchSize={BatchSize}, PollInterval={PollInterval}, Timeout={IterationTimeout}.",
            batchSize,
            pollInterval,
            iterationTimeout);

        _healthMonitor.ReportBackgroundState(MonitorServiceName, BackgroundServiceRunState.Starting);
        _healthMonitor.ReportBackgroundState(MonitorServiceName, BackgroundServiceRunState.Running);

        var consecutiveFailures = 0;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var iterationCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _lifecycleService.RunToken);
                iterationCts.CancelAfter(iterationTimeout);
                var iterationToken = iterationCts.Token;

                try
                {
                    var wasPaused = _lifecycleService.PauseToken.IsPaused;
                    if (wasPaused)
                    {
                        _healthMonitor.ReportBackgroundState(MonitorServiceName, BackgroundServiceRunState.Paused);
                        _logger.LogInformation("Reindex queue processor paused by lifecycle.");
                    }

                    await _lifecycleService.PauseToken.WaitIfPausedAsync(iterationToken).ConfigureAwait(false);

                    if (wasPaused)
                    {
                        _healthMonitor.ReportBackgroundState(MonitorServiceName, BackgroundServiceRunState.Running);
                        _logger.LogInformation("Reindex queue processor resumed after lifecycle pause.");
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

                var iterationWatch = Stopwatch.StartNew();
                BackgroundIterationOutcome outcome;
                Exception? iterationException = null;
                int processedCount = 0;

                try
                {
                    _logger.LogInformation(
                        "Reindex queue iteration starting (batch size {BatchSize}).",
                        batchSize);

                    processedCount = await ProcessBatchAsync(batchSize, iterationToken).ConfigureAwait(false);
                    outcome = processedCount > 0
                        ? BackgroundIterationOutcome.Success
                        : BackgroundIterationOutcome.NoWork;
                }
                catch (OperationCanceledException) when (iterationToken.IsCancellationRequested)
                {
                    if (stoppingToken.IsCancellationRequested || _lifecycleService.RunToken.IsCancellationRequested)
                    {
                        break;
                    }

                    if (iterationCts.IsCancellationRequested)
                    {
                        outcome = BackgroundIterationOutcome.Timeout;
                        iterationException = new TimeoutException($"Reindex iteration exceeded timeout {iterationTimeout}.");
                        _logger.LogWarning("Reindex queue iteration timed out after {Timeout}.", iterationTimeout);
                    }
                    else
                    {
                        outcome = BackgroundIterationOutcome.Canceled;
                    }
                }
                catch (Exception ex)
                {
                    outcome = BackgroundIterationOutcome.Failed;
                    iterationException = ex;
                    _logger.LogError(ex, "Reindex queue iteration failed.");
                }

                iterationWatch.Stop();

                _healthMonitor.ReportBackgroundIteration(
                    MonitorServiceName,
                    outcome,
                    iterationWatch.Elapsed,
                    iterationException,
                    processedCount > 0 ? $"Processed {processedCount} entries." : null);

                _logger.LogInformation(
                    "Reindex queue iteration completed in {Duration} with outcome {Outcome}. Processed {Processed} entries.",
                    iterationWatch.Elapsed,
                    outcome,
                    processedCount);

                switch (outcome)
                {
                    case BackgroundIterationOutcome.Timeout:
                    case BackgroundIterationOutcome.Failed:
                        consecutiveFailures++;
                        break;
                    default:
                        consecutiveFailures = 0;
                        break;
                }

                if (stoppingToken.IsCancellationRequested || _lifecycleService.RunToken.IsCancellationRequested)
                {
                    break;
                }

                var delay = CalculateDelay(outcome, pollInterval, baseErrorBackoff, consecutiveFailures);
                if (delay <= TimeSpan.Zero)
                {
                    continue;
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reindex queue processor crashed.");
            _healthMonitor.ReportBackgroundState(MonitorServiceName, BackgroundServiceRunState.Faulted, ex.Message);
            throw;
        }
        finally
        {
            _healthMonitor.ReportBackgroundState(MonitorServiceName, BackgroundServiceRunState.Stopped);
            _logger.LogInformation("Reindex queue processor stopped.");
        }
    }

    private async Task<int> ProcessBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var entries = await context.ReindexQueue
            .Where(entry => entry.ProcessedUtc == null)
            .OrderBy(entry => entry.EnqueuedUtc)
            .ThenBy(entry => entry.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (entries.Count == 0)
        {
            return 0;
        }

        var processed = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = await _coordinator
                    .ReindexAsync(entry.FileId, entry.Reason, cancellationToken)
                    .ConfigureAwait(false);

                switch (result.Status)
                {
                    case SearchIndexUpdateStatus.Succeeded:
                        entry.ProcessedUtc = now;
                        processed++;
                        break;
                    case SearchIndexUpdateStatus.NoChanges:
                        entry.ProcessedUtc = now;
                        processed++;
                        break;
                    case SearchIndexUpdateStatus.NotFound:
                        entry.ProcessedUtc = now;
                        _logger.LogWarning(
                            "Reindex queue entry {EntryId} skipped because file {FileId} was not found.",
                            entry.Id,
                            entry.FileId);
                        processed++;
                        break;
                    case SearchIndexUpdateStatus.Failed:
                        entry.RetryCount++;
                        _logger.LogWarning(
                            result.Exception,
                            "Reindex queue entry {EntryId} failed (attempt {Attempt}).",
                            entry.Id,
                            entry.RetryCount);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                entry.RetryCount++;
                _logger.LogError(
                    ex,
                    "Unexpected failure while processing reindex queue entry {EntryId}.",
                    entry.Id);
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return processed;
    }

    private TimeSpan CalculateDelay(
        BackgroundIterationOutcome outcome,
        TimeSpan pollInterval,
        TimeSpan baseErrorBackoff,
        int consecutiveFailures)
    {
        return outcome switch
        {
            BackgroundIterationOutcome.Success or BackgroundIterationOutcome.NoWork
                => AddJitter(pollInterval),
            BackgroundIterationOutcome.Timeout or BackgroundIterationOutcome.Failed
                => AddJitter(ComputeBackoff(baseErrorBackoff, consecutiveFailures)),
            _ => TimeSpan.Zero,
        };
    }

    private TimeSpan ComputeBackoff(TimeSpan baseDelay, int failures)
    {
        var multiplier = Math.Pow(2, Math.Min(failures, 6));
        var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * multiplier);
        if (delay > MaximumBackoff)
        {
            delay = MaximumBackoff;
        }

        return delay;
    }

    private TimeSpan AddJitter(TimeSpan baseDelay)
    {
        if (baseDelay <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var jitterWindow = Math.Min(baseDelay.TotalMilliseconds * 0.1, TimeSpan.FromSeconds(5).TotalMilliseconds);
        var jitter = TimeSpan.FromMilliseconds(_random.NextDouble() * jitterWindow);
        return baseDelay + jitter;
    }
}
