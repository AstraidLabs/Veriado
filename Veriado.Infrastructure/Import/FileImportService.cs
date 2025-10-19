using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    private readonly ILogger<FileImportService> _logger;

    public FileImportService(
        AppDbContext dbContext,
        IFileSearchProjection searchProjection,
        ISearchIndexSignatureCalculator signatureCalculator,
        IClock clock,
        ILogger<FileImportService>? logger = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _searchProjection = searchProjection ?? throw new ArgumentNullException(nameof(searchProjection));
        _signatureCalculator = signatureCalculator ?? throw new ArgumentNullException(nameof(signatureCalculator));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? NullLogger<FileImportService>.Instance;
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

            var stopwatch = Stopwatch.StartNew();
            var (batchResult, busyRetries) = await PersistBatchAsync(slice, options, ct).ConfigureAwait(false);
            stopwatch.Stop();

            imported += batchResult.Imported;
            skipped += batchResult.Skipped;
            updated += batchResult.Updated;

            var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
            var itemsPerSecond = stopwatch.Elapsed.TotalSeconds > 0d
                ? count / stopwatch.Elapsed.TotalSeconds
                : count;

            _logger.LogInformation(
                "Import batch persisted {BatchSize} items in {ElapsedMs:F0} ms ({ItemsPerSec:F2} items/s) with {BusyRetries} SQLITE_BUSY retries",
                count,
                elapsedMs,
                itemsPerSecond,
                busyRetries);
        }

        return new ImportResult(imported, skipped, updated);
    }

    private async Task<(ImportResult Result, int BusyRetries)> PersistBatchAsync(
        IReadOnlyList<ImportItem> batch,
        ImportOptions options,
        CancellationToken ct)
    {
        var deduped = Deduplicate(batch);
        if (deduped.Count == 0)
        {
            return (new ImportResult(0, 0, 0), 0);
        }

        var ids = deduped.Select(item => item.FileId).ToArray();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var projectionGuard = new DbContextSearchProjectionGuard(_dbContext);

        var existingFiles = await _dbContext.Files
            .AsNoTracking()
            .Where(file => ids.Contains(file.Id))
            .Include(file => file.Validity)
            .ToListAsync(ct)
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
            _dbContext.ChangeTracker.Clear();
            return (new ImportResult(imported, skipped, updated), 0);
        }

        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

        var busyRetries = 0;

        if (options.UpsertFts)
        {
            var projectionItems = new List<SearchProjectionWorkItem>(persisted.Count);

            foreach (var mapped in persisted)
            {
                ct.ThrowIfCancellationRequested();
                var signature = _signatureCalculator.Compute(mapped.File);
                var expectedContentHash = mapped.File.SearchIndex?.IndexedContentHash;
                var newContentHash = mapped.File.ContentHash.Value;

                projectionItems.Add(new SearchProjectionWorkItem(
                    mapped.File,
                    expectedContentHash,
                    mapped.File.SearchIndex?.TokenHash,
                    newContentHash,
                    signature.TokenHash,
                    signature,
                    mapped.SearchMetadata?.IndexedUtc,
                    mapped.SearchMetadata?.IndexedTitle));
            }

            if (projectionItems.Count > 0)
            {
                if (_searchProjection is IBatchFileSearchProjection batchProjection)
                {
                    var batchResult = await batchProjection
                        .UpsertBatchAsync(projectionItems, projectionGuard, ct)
                        .ConfigureAwait(false);
                    busyRetries = batchResult.BusyRetries;
                }
                else
                {
                    foreach (var item in projectionItems)
                    {
                        try
                        {
                            await _searchProjection
                                .UpsertAsync(
                                    item.File,
                                    item.ExpectedContentHash,
                                    item.ExpectedTokenHash,
                                    item.NewContentHash,
                                    item.TokenHash,
                                    projectionGuard,
                                    ct)
                                .ConfigureAwait(false);
                        }
                        catch (AnalyzerOrContentDriftException)
                        {
                            await _searchProjection
                                .ForceReplaceAsync(
                                    item.File,
                                    item.NewContentHash,
                                    item.TokenHash,
                                    projectionGuard,
                                    ct)
                                .ConfigureAwait(false);
                        }
                    }
                }

                foreach (var item in projectionItems)
                {
                    var indexedAt = item.IndexedUtc ?? _clock.UtcNow;
                    item.File.ConfirmIndexed(
                        item.File.SearchIndex?.SchemaVersion ?? 1,
                        UtcTimestamp.From(indexedAt),
                        item.Signature.AnalyzerVersion,
                        item.Signature.TokenHash,
                        item.IndexedTitle ?? item.Signature.NormalizedTitle);
                }

                await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        _dbContext.ChangeTracker.Clear();
        return (new ImportResult(imported, skipped, updated), busyRetries);
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
