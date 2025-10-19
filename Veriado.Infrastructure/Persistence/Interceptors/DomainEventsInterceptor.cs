using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
    private readonly ConcurrentDictionary<DbContextId, bool> _suppressed = new();

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
        CapturePendingEvents(eventData);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        CapturePendingEvents(eventData);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await ProcessPendingEventsAsync(eventData, cancellationToken).ConfigureAwait(false);
        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        ResetPendingState(eventData);
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ResetPendingState(eventData);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void CapturePendingEvents(DbContextEventData eventData)
    {
        if (eventData.Context is not AppDbContext context)
        {
            return;
        }

        var contextId = context.ContextId;
        if (_suppressed.ContainsKey(contextId))
        {
            return;
        }

        var batch = new DomainEventBatch();
        CaptureAggregateVersions(context, batch);
        CaptureDomainEvents(context, batch);

        if (batch.HasData)
        {
            _pending[contextId] = batch;
        }
    }

    private static void CaptureAggregateVersions(AppDbContext context, DomainEventBatch batch)
    {
        foreach (var entry in context.ChangeTracker.Entries<AggregateRoot>())
        {
            if (entry.State is not (EntityState.Modified or EntityState.Added))
            {
                continue;
            }

            var versionProperty = entry.Property(root => root.Version);
            var originalVersion = versionProperty.CurrentValue;
            batch.AggregateVersions.Add(new AggregateVersionSnapshot(entry, originalVersion));
            entry.Entity.IncrementVersion();
            versionProperty.IsModified = true;
        }
    }

    private static void CaptureDomainEvents(AppDbContext context, DomainEventBatch batch)
    {
        foreach (var entry in context.ChangeTracker.Entries<EntityBase>())
        {
            if (entry.Entity.DomainEvents.Count == 0)
            {
                continue;
            }

            foreach (var domainEvent in entry.Entity.DomainEvents)
            {
                batch.DomainEvents.Add(new PendingDomainEvent(entry.Entity, entry.Entity.Id, domainEvent));
            }
        }
    }

    private async Task ProcessPendingEventsAsync(SaveChangesCompletedEventData eventData, CancellationToken cancellationToken)
    {
        if (eventData.Context is not AppDbContext context)
        {
            return;
        }

        var contextId = context.ContextId;
        if (_suppressed.TryRemove(contextId, out _))
        {
            return;
        }

        if (!_pending.TryRemove(contextId, out var batch))
        {
            return;
        }

        if (batch.DomainEvents.Count == 0)
        {
            return;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Dispatching {EventCount} domain events for context {ContextId}",
                batch.DomainEvents.Count,
                contextId);
        }

        var domainEventTuples = batch.DomainEvents
            .Select(evt => (evt.AggregateId, evt.DomainEvent))
            .ToList();

        var hasAuditChanges = _auditProjector.Project(context, domainEventTuples);

        await _dispatcher
            .DispatchAsync(context, domainEventTuples.Select(tuple => tuple.DomainEvent).ToList(), cancellationToken)
            .ConfigureAwait(false);

        var logs = CreateLogEntries(domainEventTuples);
        if (logs.Count > 0)
        {
            await context.DomainEventLog.AddRangeAsync(logs, cancellationToken).ConfigureAwait(false);
        }

        if (context.ChangeTracker.HasChanges())
        {
            try
            {
                _suppressed[contextId] = true;
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _suppressed.TryRemove(contextId, out _);
            }
        }

        foreach (var group in batch.DomainEvents.GroupBy(evt => evt.Source))
        {
            group.Key.ClearDomainEvents();
        }
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

    private void ResetPendingState(DbContextEventData eventData)
    {
        if (eventData.Context is not AppDbContext context)
        {
            return;
        }

        var contextId = context.ContextId;
        _suppressed.TryRemove(contextId, out _);

        if (!_pending.TryRemove(contextId, out var batch))
        {
            return;
        }

        foreach (var snapshot in batch.AggregateVersions)
        {
            snapshot.Entry.Entity.SetVersion(snapshot.OriginalVersion);
            snapshot.Entry.Property(root => root.Version).CurrentValue = snapshot.OriginalVersion;
            snapshot.Entry.Property(root => root.Version).IsModified = false;
        }
    }

    private sealed class DomainEventBatch
    {
        public List<PendingDomainEvent> DomainEvents { get; } = new();
        public List<AggregateVersionSnapshot> AggregateVersions { get; } = new();
        public bool HasData => DomainEvents.Count > 0 || AggregateVersions.Count > 0;
    }

    private sealed record PendingDomainEvent(EntityBase Source, Guid AggregateId, IDomainEvent DomainEvent);

    private sealed record AggregateVersionSnapshot(EntityEntry<AggregateRoot> Entry, ulong OriginalVersion);
}
