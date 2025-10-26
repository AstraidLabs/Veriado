using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Veriado.Domain.Primitives;
using Veriado.Infrastructure.Events;
using Veriado.Infrastructure.Persistence.EventLog;

namespace Veriado.Infrastructure.Persistence.Interceptors;

internal sealed class DomainEventsInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions EventSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly AuditEventProjector _auditProjector;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly ILogger<DomainEventsInterceptor> _logger;
    private readonly ConcurrentDictionary<DbContextId, DomainEventBatch> _pending = new();

    public DomainEventsInterceptor(
        AuditEventProjector auditProjector,
        IDomainEventDispatcher dispatcher,
        ILogger<DomainEventsInterceptor> logger)
    {
        _auditProjector = auditProjector ?? throw new ArgumentNullException(nameof(auditProjector));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is AppDbContext context)
        {
            ProcessDomainEventsAsync(context, CancellationToken.None).GetAwaiter().GetResult();
        }

        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is AppDbContext context)
        {
            await ProcessDomainEventsAsync(context, cancellationToken).ConfigureAwait(false);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is AppDbContext context)
        {
            CompletePendingState(context);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is AppDbContext context)
        {
            ResetPendingState(context);
        }

        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is AppDbContext context)
        {
            ResetPendingState(context);
        }

        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private async Task ProcessDomainEventsAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var contextId = context.ContextId;
        if (_pending.ContainsKey(contextId))
        {
            return;
        }

        var batch = new DomainEventBatch();
        _pending[contextId] = batch;

        try
        {
            while (true)
            {
                var newEvents = CaptureDomainEvents(context, batch);
                if (newEvents.Count == 0)
                {
                    break;
                }

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Dispatching {EventCount} domain events for context {ContextId}",
                        newEvents.Count,
                        contextId);
                }

                var domainEventTuples = newEvents
                    .Select(evt => (evt.AggregateId, evt.DomainEvent))
                    .ToList();

                await _dispatcher
                    .DispatchAsync(context, domainEventTuples.Select(tuple => tuple.DomainEvent).ToList(), cancellationToken)
                    .ConfigureAwait(false);

                var logs = CreateLogEntries(domainEventTuples);
                if (logs.Count > 0)
                {
                    batch.EventLogs.AddRange(logs);
                    await context.DomainEventLog.AddRangeAsync(logs, cancellationToken).ConfigureAwait(false);
                }

                _auditProjector.Project(context, domainEventTuples);
            }

            if (!batch.HasData)
            {
                _pending.TryRemove(contextId, out _);
            }
        }
        catch
        {
            RestoreState(context, batch);
            _pending.TryRemove(contextId, out _);
            throw;
        }
    }

    private static List<PendingDomainEvent> CaptureDomainEvents(AppDbContext context, DomainEventBatch batch)
    {
        var captured = new List<PendingDomainEvent>();

        foreach (var entry in context.ChangeTracker.Entries<EntityBase>())
        {
            if (entry.Entity.DomainEvents.Count == 0)
            {
                continue;
            }

            foreach (var domainEvent in entry.Entity.DomainEvents)
            {
                if (!batch.ProcessedEvents.Add(domainEvent))
                {
                    continue;
                }

                var pending = new PendingDomainEvent(entry.Entity, entry.Entity.Id, domainEvent);
                batch.DomainEvents.Add(pending);
                captured.Add(pending);
            }
        }

        return captured;
    }

    private static List<DomainEventLogEntry> CreateLogEntries(IReadOnlyList<(Guid AggregateId, IDomainEvent DomainEvent)> events)
    {
        var logs = new List<DomainEventLogEntry>(events.Count);

        foreach (var (aggregateId, domainEvent) in events)
        {
            var eventType = domainEvent.GetType();
            logs.Add(new DomainEventLogEntry
            {
                EventType = eventType.FullName ?? eventType.Name,
                EventJson = JsonSerializer.Serialize(domainEvent, eventType, EventSerializerOptions),
                AggregateId = aggregateId.ToString("D", CultureInfo.InvariantCulture),
                OccurredUtc = domainEvent.OccurredOnUtc,
            });
        }

        return logs;
    }

    private void CompletePendingState(AppDbContext context)
    {
        var contextId = context.ContextId;

        if (!_pending.TryRemove(contextId, out var batch))
        {
            return;
        }

        foreach (var group in batch.DomainEvents.GroupBy(evt => evt.Source))
        {
            group.Key.ClearDomainEvents();
        }
    }

    private void ResetPendingState(AppDbContext context)
    {
        var contextId = context.ContextId;

        if (!_pending.TryRemove(contextId, out var batch))
        {
            return;
        }

        RestoreState(context, batch);
    }

    private static void RestoreState(AppDbContext context, DomainEventBatch batch)
    {
        foreach (var log in batch.EventLogs)
        {
            var entry = context.Entry(log);
            if (entry.State == EntityState.Added)
            {
                entry.State = EntityState.Detached;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    private sealed class DomainEventBatch
    {
        public List<PendingDomainEvent> DomainEvents { get; } = new();

        public HashSet<IDomainEvent> ProcessedEvents { get; } = new(ReferenceEqualityComparer.Instance);

        public List<DomainEventLogEntry> EventLogs { get; } = new();

        public bool HasData => DomainEvents.Count > 0 || EventLogs.Count > 0;
    }

    private sealed record PendingDomainEvent(EntityBase Source, Guid AggregateId, IDomainEvent DomainEvent);
}
