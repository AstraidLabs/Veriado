using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Veriado.Appl.Abstractions;
using Veriado.Application.Import;
using Veriado.Domain.FileSystem;
using Veriado.Domain.Files;
using Veriado.Domain.Primitives;
using Veriado.Domain.Search.Events;
using Veriado.Domain.ValueObjects;
using Veriado.Infrastructure.Events;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.EventLog;

namespace Veriado.Infrastructure.Import;

/// <summary>
/// Provides a high-throughput import pipeline for file aggregates.
/// </summary>
public sealed class FileImportService : IFileImportWriter
{
    private const int MinimumBatchSize = 500;
    private const int MaximumBatchSize = 2000;

    private static readonly JsonSerializerOptions EventSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _dbContext;
    private readonly IFileSearchProjection _searchProjection;
    private readonly ISearchIndexSignatureCalculator _signatureCalculator;
    private readonly IClock _clock;
    private readonly AuditEventProjector _auditProjector;

    public FileImportService(
        AppDbContext dbContext,
        IFileSearchProjection searchProjection,
        ISearchIndexSignatureCalculator signatureCalculator,
        IClock clock,
        AuditEventProjector auditProjector)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _searchProjection = searchProjection ?? throw new ArgumentNullException(nameof(searchProjection));
        _signatureCalculator = signatureCalculator ?? throw new ArgumentNullException(nameof(signatureCalculator));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _auditProjector = auditProjector ?? throw new ArgumentNullException(nameof(auditProjector));
    }

    public async Task<ImportResult> ImportAsync(
        IReadOnlyList<ImportItem> items,
        ImportOptions options,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(options);
        ct.ThrowIfCancellationRequested();

        if (items.Count == 0)
        {
            return new ImportResult(0, 0, 0);
        }

        var normalizedBatchSize = Math.Clamp(options.BatchSize, MinimumBatchSize, MaximumBatchSize);
        var imported = 0;
        var skipped = 0;
        var updated = 0;

        for (var offset = 0; offset < items.Count; offset += normalizedBatchSize)
        {
            ct.ThrowIfCancellationRequested();
            var count = Math.Min(normalizedBatchSize, items.Count - offset);
            var slice = new ImportItem[count];
            for (var i = 0; i < count; i++)
            {
                slice[i] = items[offset + i];
            }

            var batchResult = await PersistBatchAsync(slice, options, ct).ConfigureAwait(false);
            imported += batchResult.Imported;
            skipped += batchResult.Skipped;
            updated += batchResult.Updated;

            if (options.DetachAfterBatch)
            {
                _dbContext.ChangeTracker.Clear();
            }
        }

        return new ImportResult(imported, skipped, updated);
    }

    private async Task<ImportResult> PersistBatchAsync(
        IReadOnlyList<ImportItem> batch,
        ImportOptions options,
        CancellationToken ct)
    {
        var deduped = Deduplicate(batch);
        if (deduped.Count == 0)
        {
            return new ImportResult(0, 0, 0);
        }

        var ids = deduped.Select(item => item.FileId).ToArray();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        var existingFiles = await _dbContext.Files
            .Where(file => ids.Contains(file.Id))
            .Include(file => file.Validity)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var existingFileSystemIds = existingFiles
            .Select(file => file.FileSystemId)
            .Distinct()
            .ToArray();

        var existingFileSystems = await _dbContext.FileSystems
            .Where(fs => existingFileSystemIds.Contains(fs.Id))
            .ToDictionaryAsync(fs => fs.Id, ct)
            .ConfigureAwait(false);

        var imported = 0;
        var skipped = 0;
        var updated = 0;
        var persisted = new List<MappedImport>(deduped.Count);

        foreach (var item in deduped)
        {
            ct.ThrowIfCancellationRequested();
            var existing = existingFiles.FirstOrDefault(file => file.Id == item.FileId);
            var mapped = ImportMapping.MapToAggregate(item, existing is null, existing?.FileSystemId);

            if (existing is not null)
            {
                if (existing.ContentRevision >= mapped.Version)
                {
                    skipped++;
                    continue;
                }

                if (existing.FileSystemId != Guid.Empty
                    && existingFileSystems.TryGetValue(existing.FileSystemId, out var existingFs))
                {
                    _dbContext.Entry(existingFs).State = EntityState.Detached;
                }

                _dbContext.Entry(existing).State = EntityState.Detached;
                _dbContext.FileSystems.Update(mapped.FileSystem);
                _dbContext.Files.Update(mapped.File);
                updated++;
            }
            else
            {
                _dbContext.FileSystems.Add(mapped.FileSystem);
                _dbContext.Files.Add(mapped.File);
                imported++;
            }

            persisted.Add(mapped);
        }

        if (persisted.Count == 0)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return new ImportResult(imported, skipped, updated);
        }

        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        await PersistDomainEventsAsync(persisted, ct).ConfigureAwait(false);

        if (options.UpsertFts)
        {
            foreach (var mapped in persisted)
            {
                ct.ThrowIfCancellationRequested();
                await _searchProjection.UpsertAsync(mapped.File, ct).ConfigureAwait(false);

                var signature = _signatureCalculator.Compute(mapped.File);
                var indexedAt = mapped.SearchMetadata?.IndexedUtc ?? _clock.UtcNow;
                mapped.File.ConfirmIndexed(
                    mapped.File.SearchIndex?.SchemaVersion ?? 1,
                    UtcTimestamp.From(indexedAt),
                    signature.AnalyzerVersion,
                    signature.TokenHash,
                    mapped.SearchMetadata?.IndexedTitle ?? signature.NormalizedTitle);
            }

            await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new ImportResult(imported, skipped, updated);
    }

    private async Task PersistDomainEventsAsync(IReadOnlyList<MappedImport> persisted, CancellationToken ct)
    {
        if (persisted.Count == 0)
        {
            return;
        }

        var domainEvents = new List<(Guid AggregateId, IDomainEvent DomainEvent)>();

        foreach (var mapped in persisted)
        {
            CollectDomainEvents(mapped.File, domainEvents);
            CollectDomainEvents(mapped.FileSystem, domainEvents);
        }

        if (domainEvents.Count == 0)
        {
            ClearDomainEvents(persisted);
            return;
        }

        await StoreDomainEventsAsync(_dbContext, _auditProjector, domainEvents, ct).ConfigureAwait(false);
        ClearDomainEvents(persisted);
    }

    private static void CollectDomainEvents(EntityBase entity, List<(Guid AggregateId, IDomainEvent DomainEvent)> domainEvents)
    {
        foreach (var domainEvent in entity.DomainEvents)
        {
            domainEvents.Add((entity.Id, domainEvent));
        }
    }

    private static void ClearDomainEvents(IReadOnlyList<MappedImport> persisted)
    {
        foreach (var mapped in persisted)
        {
            mapped.File.ClearDomainEvents();
            mapped.FileSystem.ClearDomainEvents();
        }
    }

    private static async Task StoreDomainEventsAsync(
        AppDbContext context,
        AuditEventProjector auditProjector,
        IReadOnlyList<(Guid AggregateId, IDomainEvent DomainEvent)> domainEvents,
        CancellationToken cancellationToken)
    {
        if (domainEvents.Count == 0)
        {
            return;
        }

        var logs = new List<DomainEventLogEntry>(domainEvents.Count);

        foreach (var (aggregateId, domainEvent) in domainEvents)
        {
            var eventType = domainEvent.GetType();
            logs.Add(new DomainEventLogEntry
            {
                EventType = eventType.FullName ?? eventType.Name,
                EventJson = JsonSerializer.Serialize(domainEvent, eventType, EventSerializerOptions),
                AggregateId = aggregateId.ToString("D", CultureInfo.InvariantCulture),
                OccurredUtc = domainEvent.OccurredOnUtc,
            });

            if (domainEvent is SearchReindexRequested)
            {
                // TODO(FTS Sync): Handle SearchReindexRequested synchronously once FTS updates run inside the transaction.
            }
        }

        var hasAuditChanges = auditProjector.Project(context, domainEvents);

        if (logs.Count > 0)
        {
            await context.DomainEventLog.AddRangeAsync(logs, cancellationToken).ConfigureAwait(false);
        }

        if (logs.Count > 0 || hasAuditChanges)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static IReadOnlyList<ImportItem> Deduplicate(IReadOnlyList<ImportItem> items)
    {
        if (items.Count == 0)
        {
            return Array.Empty<ImportItem>();
        }

        var order = new List<ImportItem>(items.Count);
        var index = new Dictionary<Guid, int>();

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (index.TryGetValue(item.FileId, out var existingIndex))
            {
                order[existingIndex] = item;
            }
            else
            {
                index[item.FileId] = order.Count;
                order.Add(item);
            }
        }

        return order;
    }
}
