using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
    private readonly ISearchProjectionScope _projectionScope;

    public FileImportService(
        AppDbContext dbContext,
        IFileSearchProjection searchProjection,
        ISearchIndexSignatureCalculator signatureCalculator,
        IClock clock,
        ISearchProjectionScope projectionScope,
        ILogger<FileImportService>? logger = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _searchProjection = searchProjection ?? throw new ArgumentNullException(nameof(searchProjection));
        _signatureCalculator = signatureCalculator ?? throw new ArgumentNullException(nameof(signatureCalculator));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _projectionScope = projectionScope ?? throw new ArgumentNullException(nameof(projectionScope));
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
        var currentTransaction = _dbContext.Database.CurrentTransaction;
        var ownsTransaction = currentTransaction is null;
        IDbContextTransaction? transaction = currentTransaction;

        if (ownsTransaction)
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        }

        _projectionScope.EnsureActive();

        try
        {
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
                if (ownsTransaction)
                {
                    await transaction!.CommitAsync(ct).ConfigureAwait(false);
                    _dbContext.ChangeTracker.Clear();
                }
                return (new ImportResult(imported, skipped, updated), 0);
            }

            try
            {
                await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new FileConcurrencyException("The file was modified by another operation during import.", ex);
            }

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
                    var projectedFiles = new HashSet<Guid>();

                    if (_searchProjection is IBatchFileSearchProjection batchProjection)
                    {
                        var batchResult = await batchProjection
                            .UpsertBatchAsync(projectionItems, _projectionScope, ct)
                            .ConfigureAwait(false);
                        busyRetries = batchResult.BusyRetries;

                        if (batchResult.ProjectedItems == projectionItems.Count)
                        {
                            foreach (var item in projectionItems)
                            {
                                projectedFiles.Add(item.File.Id);
                            }
                        }
                    }
                    else
                    {
                        await _projectionScope
                            .ExecuteAsync(
                                async scopeCt =>
                                {
                                    foreach (var item in projectionItems)
                                    {
                                        scopeCt.ThrowIfCancellationRequested();

                                        try
                                        {
                                            var projected = await _searchProjection
                                                .UpsertAsync(
                                                    item.File,
                                                    item.ExpectedContentHash,
                                                    item.ExpectedTokenHash,
                                                    item.NewContentHash,
                                                    item.TokenHash,
                                                    _projectionScope,
                                                    scopeCt)
                                                .ConfigureAwait(false);
                                            if (projected)
                                            {
                                                projectedFiles.Add(item.File.Id);
                                            }
                                        }
                                        catch (AnalyzerOrContentDriftException)
                                        {
                                            var projected = await _searchProjection
                                                .ForceReplaceAsync(
                                                    item.File,
                                                    item.NewContentHash,
                                                    item.TokenHash,
                                                    _projectionScope,
                                                    scopeCt)
                                                .ConfigureAwait(false);
                                            if (projected)
                                            {
                                                projectedFiles.Add(item.File.Id);
                                            }
                                        }
                                    }
                                },
                                ct)
                            .ConfigureAwait(false);
                    }

                    foreach (var item in projectionItems)
                    {
                        if (!projectedFiles.Contains(item.File.Id))
                        {
                            continue;
                        }

                        var indexedAt = item.IndexedUtc ?? _clock.UtcNow;
                        item.File.ConfirmIndexed(
                            item.File.SearchIndex?.SchemaVersion ?? 1,
                            UtcTimestamp.From(indexedAt),
                            item.Signature.AnalyzerVersion,
                            item.Signature.TokenHash,
                            item.IndexedTitle ?? item.Signature.NormalizedTitle);
                    }

                    try
                    {
                        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
                    }
                    catch (DbUpdateConcurrencyException ex)
                    {
                        throw new FileConcurrencyException("The file was modified by another operation during import.", ex);
                    }
                }
            }

            if (ownsTransaction)
            {
                await transaction!.CommitAsync(ct).ConfigureAwait(false);
                _dbContext.ChangeTracker.Clear();
            }
            return (new ImportResult(imported, skipped, updated), busyRetries);
        }
        finally
        {
            if (ownsTransaction && transaction is not null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
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
