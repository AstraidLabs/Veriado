using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Veriado.Appl.Abstractions;
using Veriado.Infrastructure.Concurrency;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Options;
using Veriado.Infrastructure.Persistence.Outbox;

namespace Veriado.Infrastructure.Events;

/// <summary>
/// Hosted service responsible for dispatching persisted domain events from the outbox.
/// </summary>
internal sealed class OutboxDispatcherHostedService : BackgroundService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IEventPublisher _eventPublisher;
    private readonly InfrastructureOptions _options;
    private readonly IWritePipelineTelemetry _telemetry;
    private readonly ILogger<OutboxDispatcherHostedService> _logger;
    private readonly IClock _clock;

    public OutboxDispatcherHostedService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IEventPublisher eventPublisher,
        InfrastructureOptions options,
        IWritePipelineTelemetry telemetry,
        ILogger<OutboxDispatcherHostedService> logger,
        IClock clock)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Outbox dispatcher started (interval={Interval}, batch={BatchSize}, maxAttempts={MaxAttempts}).",
            _options.OutboxDispatchInterval,
            _options.OutboxMaxBatchSize,
            _options.OutboxMaxAttempts);

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = false;
            try
            {
                processed = await DispatchPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox dispatcher iteration failed unexpectedly.");
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (!processed)
            {
                try
                {
                    await Task.Delay(_options.OutboxDispatchInterval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("Outbox dispatcher stopped.");
    }

    private async Task<bool> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var batchSize = Math.Max(1, _options.OutboxMaxBatchSize);
        var now = _clock.UtcNow;

        var candidates = await context.OutboxEvents
            .Where(entry => entry.Attempts < _options.OutboxMaxAttempts)
            .OrderBy(entry => entry.CreatedUtc)
            .Take(batchSize * 4)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            return false;
        }

        var ready = candidates
            .Where(entry => IsReady(entry, now))
            .Take(batchSize)
            .ToList();

        if (ready.Count == 0)
        {
            return false;
        }

        var removedAny = false;

        foreach (var entry in ready)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!OutboxDomainEventSerializer.TryDeserialize(entry, out var domainEvent, out var error) || domainEvent is null)
            {
                entry.Attempts++;
                entry.LastError = error ?? "Failed to deserialize outbox payload.";
                _telemetry.RecordOutboxFailure();
                LogFailure(entry, entry.LastError!, isTerminal: entry.Attempts >= _options.OutboxMaxAttempts);
                continue;
            }

            try
            {
                await _eventPublisher.PublishAsync(new[] { domainEvent }, cancellationToken).ConfigureAwait(false);
                context.OutboxEvents.Remove(entry);
                removedAny = true;
                var latency = _clock.UtcNow - entry.CreatedUtc;
                _telemetry.RecordOutboxDelivery(latency);
                _logger.LogDebug(
                    "Dispatched outbox event {OutboxId} (type={Type}, latencyMs={Latency}).",
                    entry.Id,
                    entry.Type,
                    Math.Max(0d, latency.TotalMilliseconds));
            }
            catch (Exception ex)
            {
                entry.Attempts++;
                entry.LastError = ex.ToString();
                _telemetry.RecordOutboxFailure();
                LogFailure(entry, ex.Message, entry.Attempts >= _options.OutboxMaxAttempts, ex);
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return removedAny;
    }

    private void LogFailure(OutboxEventEntity entry, string message, bool isTerminal, Exception? exception = null)
    {
        var attempts = entry.Attempts;
        if (isTerminal)
        {
            _logger.LogError(
                exception,
                "Outbox event {OutboxId} (type={Type}) reached maximum attempts {Attempts}. LastError={Error}",
                entry.Id,
                entry.Type,
                attempts,
                message);
        }
        else
        {
            _logger.LogWarning(
                exception,
                "Outbox event {OutboxId} (type={Type}) dispatch failed on attempt {Attempts}: {Error}",
                entry.Id,
                entry.Type,
                attempts,
                message);
        }
    }

    private bool IsReady(OutboxEventEntity entry, DateTimeOffset now)
    {
        if (entry.Attempts <= 0)
        {
            return true;
        }

        var backoff = CalculateBackoff(entry.Attempts);
        var due = entry.CreatedUtc + backoff;
        return now >= due;
    }

    private TimeSpan CalculateBackoff(int attempts)
    {
        if (attempts <= 0)
        {
            return TimeSpan.Zero;
        }

        var baseDelayMs = _options.OutboxInitialBackoff.TotalMilliseconds;
        var maxDelayMs = _options.OutboxMaxBackoff.TotalMilliseconds;
        var factor = Math.Pow(2, attempts) - 1d;
        var delayMs = Math.Min(maxDelayMs, baseDelayMs * factor);
        return TimeSpan.FromMilliseconds(Math.Max(delayMs, baseDelayMs));
    }
}
