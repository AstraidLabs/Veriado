using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Veriado.Appl.Abstractions;
using Veriado.Appl.Common.Exceptions;
using Veriado.Application.Import;
using Veriado.Domain.FileSystem;
using Veriado.Domain.Files;
using Veriado.Domain.ValueObjects;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Search;

namespace Veriado.Infrastructure.Import;

/// <summary>
/// Provides a high-throughput import pipeline for file aggregates.
/// </summary>
public sealed class FileImportService : IFileImportWriter
{
    private const int MinimumBatchSize = 500;
    private const int MaximumBatchSize = 2000;

    private readonly AppDbContext _dbContext;
    private readonly IFileSearchProjection _searchProjection;
    private readonly ISearchIndexSignatureCalculator _signatureCalculator;
    private readonly IClock _clock;

    public FileImportService(
        AppDbContext dbContext,
        IFileSearchProjection searchProjection,
        ISearchIndexSignatureCalculator signatureCalculator,
        IClock clock)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _searchProjection = searchProjection ?? throw new ArgumentNullException(nameof(searchProjection));
        _signatureCalculator = signatureCalculator ?? throw new ArgumentNullException(nameof(signatureCalculator));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
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
        var projectionGuard = new DbContextSearchProjectionGuard(_dbContext);

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
            var mapped = ImportMapping.MapToAggregate(item, existing?.FileSystemId);

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

        if (options.UpsertFts)
        {
            foreach (var mapped in persisted)
            {
                ct.ThrowIfCancellationRequested();
                var signature = _signatureCalculator.Compute(mapped.File);
                var expectedContentHash = mapped.File.SearchIndex?.IndexedContentHash;
                var newContentHash = mapped.File.ContentHash.Value;

                try
                {
                    await _searchProjection
                        .UpsertAsync(
                            mapped.File,
                            expectedContentHash,
                            mapped.File.SearchIndex?.TokenHash,
                            newContentHash,
                            signature.TokenHash,
                            projectionGuard,
                            ct)
                        .ConfigureAwait(false);
                }
                catch (AnalyzerOrContentDriftException)
                {
                    await _searchProjection
                        .ForceReplaceAsync(
                            mapped.File,
                            newContentHash,
                            signature.TokenHash,
                            projectionGuard,
                            ct)
                        .ConfigureAwait(false);
                }

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
