using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veriado.Application.Abstractions;
using Veriado.Domain.Files;
using Veriado.Domain.Primitives;
using Veriado.Domain.Search.Events;
using Veriado.Infrastructure.MetadataStore.Kv;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Options;
using Veriado.Infrastructure.Search;
using Veriado.Infrastructure.Search.Outbox;

namespace Veriado.Infrastructure.Concurrency;

/// <summary>
/// Background service that processes enqueued write operations in batches.
/// </summary>
internal sealed class WriteWorker : BackgroundService
{
    private readonly IWriteQueue _writeQueue;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ILogger<WriteWorker> _logger;
    private readonly InfrastructureOptions _options;
    private readonly ITextExtractor _textExtractor;
    private readonly IEventPublisher _eventPublisher;
    private readonly IClock _clock;

    public WriteWorker(
        IWriteQueue writeQueue,
        IDbContextFactory<AppDbContext> dbContextFactory,
        ILogger<WriteWorker> logger,
        InfrastructureOptions options,
        ITextExtractor textExtractor,
        IEventPublisher eventPublisher,
        IClock clock)
    {
        _writeQueue = writeQueue;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
        _options = options;
        _textExtractor = textExtractor;
        _eventPublisher = eventPublisher;
        _clock = clock;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Write worker started");
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var batch = await CollectBatchAsync(stoppingToken).ConfigureAwait(false);
                if (batch.Count == 0)
                {
                    continue;
                }

                try
                {
                    await ProcessBatchAsync(batch, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process write batch of size {BatchSize}", batch.Count);
                    foreach (var request in batch)
                    {
                        request.TrySetException(ex);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Write worker stopping");
        }
    }

    private async Task<List<WriteRequest>> CollectBatchAsync(CancellationToken cancellationToken)
    {
        var batch = new List<WriteRequest>(_options.BatchSize);
        WriteRequest? first;
        try
        {
            first = await _writeQueue.DequeueAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return batch;
        }

        if (first is null)
        {
            return batch;
        }

        batch.Add(first);
        using var windowCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        windowCts.CancelAfter(TimeSpan.FromMilliseconds(_options.BatchWindowMs));

        while (batch.Count < _options.BatchSize)
        {
            try
            {
                var next = await _writeQueue.DequeueAsync(windowCts.Token).ConfigureAwait(false);
                if (next is null)
                {
                    break;
                }

                batch.Add(next);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return batch;
    }

    private async Task ProcessBatchAsync(IReadOnlyList<WriteRequest> batch, CancellationToken cancellationToken)
    {
        var batchStopwatch = Stopwatch.StartNew();
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var results = new object?[batch.Count];
        Exception? failure = null;

        for (var index = 0; index < batch.Count; index++)
        {
            var request = batch[index];
            try
            {
                results[index] = await request.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException oce)
            {
                failure = oce;
                break;
            }
            catch (Exception ex)
            {
                failure = ex;
                break;
            }
        }

        if (failure is not null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            foreach (var request in batch)
            {
                request.TrySetException(failure);
            }

            return;
        }

        var fileEntries = context.ChangeTracker.Entries<FileEntity>().ToList();
        var filesToIndex = new List<FileEntity>();
        var filesToDelete = new List<Guid>();
        foreach (var entry in fileEntries)
        {
            if (entry.State == EntityState.Deleted)
            {
                filesToDelete.Add(entry.Entity.Id);
            }
            else if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
            {
                if (entry.Entity.SearchIndex.IsStale)
                {
                    filesToIndex.Add(entry.Entity);
                }
            }
        }

        var staleCount = filesToIndex.Count;
        if (staleCount > 0)
        {
            _logger.LogDebug("Processing {Count} stale search index entries", staleCount);
        }

        var domainEvents = fileEntries.SelectMany(entry => entry.Entity.DomainEvents).ToList();
        var reindexEvents = domainEvents.OfType<SearchReindexRequested>().ToList();

        if (_options.FtsIndexingMode == FtsIndexingMode.Outbox)
        {
            foreach (var reindex in reindexEvents)
            {
                var outbox = OutboxEvent.From(nameof(SearchReindexRequested), new
                {
                    reindex.FileId,
                    Reason = reindex.Reason.ToString(),
                }, reindex.OccurredOnUtc);
                context.OutboxEvents.Add(outbox);
            }
        }

        if (_options.UseKvMetadata)
        {
            await SynchronizeExtendedMetadataAsync(context, fileEntries, cancellationToken).ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (_options.FtsIndexingMode == FtsIndexingMode.SameTransaction)
        {
            await ApplyFulltextUpdatesAsync(context, filesToIndex, filesToDelete, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        batchStopwatch.Stop();

        foreach (var entry in fileEntries)
        {
            entry.Entity.ClearDomainEvents();
        }

        for (var index = 0; index < batch.Count; index++)
        {
            batch[index].TrySetResult(results[index]);
        }

        if (domainEvents.Count > 0)
        {
            try
            {
                await _eventPublisher.PublishAsync(domainEvents, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var eventTypes = string.Join(", ", domainEvents.Select(domainEvent => domainEvent.GetType().Name));
                _logger.LogError(ex, "Domain event publication failed for {EventTypes}", eventTypes);
            }
        }

        _logger.LogInformation(
            "Processed write batch of {BatchSize} items in {ElapsedMilliseconds} ms (indexed {IndexedCount})",
            batch.Count,
            batchStopwatch.Elapsed.TotalMilliseconds,
            staleCount);
    }

    private async Task ApplyFulltextUpdatesAsync(AppDbContext context, IReadOnlyList<FileEntity> filesToIndex, IReadOnlyList<Guid> filesToDelete, CancellationToken cancellationToken)
    {
        if (filesToIndex.Count == 0 && filesToDelete.Count == 0)
        {
            return;
        }

        if (context.Database.CurrentTransaction?.GetDbTransaction() is not SqliteTransaction sqliteTransaction)
        {
            throw new InvalidOperationException("SQLite transaction is required for full-text updates.");
        }

        var sqliteConnection = (SqliteConnection)sqliteTransaction.Connection!;
        var helper = new SqliteFts5Transactional();

        foreach (var id in filesToDelete)
        {
            await ExecuteWithRetryAsync(
                ct => helper.DeleteAsync(id, sqliteConnection, sqliteTransaction, ct),
                $"delete index for {id}",
                cancellationToken).ConfigureAwait(false);
        }

        if (filesToIndex.Count == 0)
        {
            return;
        }

        var now = _clock.UtcNow;
        foreach (var file in filesToIndex)
        {
            var stopwatch = Stopwatch.StartNew();
            var text = await _textExtractor.ExtractTextAsync(file, cancellationToken).ConfigureAwait(false);
            var document = file.ToSearchDocument(text);
            await ExecuteWithRetryAsync(
                ct => helper.IndexAsync(document, sqliteConnection, sqliteTransaction, ct),
                $"index file {file.Id}",
                cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            _logger.LogDebug("Indexed file {FileId} in {ElapsedMilliseconds} ms", file.Id, stopwatch.Elapsed.TotalMilliseconds);
            file.ConfirmIndexed(file.SearchIndex.SchemaVersion, now);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SynchronizeExtendedMetadataAsync(
        AppDbContext context,
        IReadOnlyList<EntityEntry<FileEntity>> fileEntries,
        CancellationToken cancellationToken)
    {
        if (fileEntries.Count == 0)
        {
            return;
        }

        var processed = new HashSet<Guid>();
        foreach (var entry in fileEntries)
        {
            if (!processed.Add(entry.Entity.Id))
            {
                continue;
            }

            var fileId = entry.Entity.Id;
            switch (entry.State)
            {
                case EntityState.Added:
                case EntityState.Modified:
                    await context.ExtendedMetadataEntries
                        .Where(metadata => metadata.FileId == fileId)
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);

                    var mapped = ExtMetadataMapper.ToEntries(fileId, entry.Entity.ExtendedMetadata);
                    if (mapped.Count > 0)
                    {
                        await context.ExtendedMetadataEntries
                            .AddRangeAsync(mapped, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    break;

                case EntityState.Deleted:
                    await context.ExtendedMetadataEntries
                        .Where(metadata => metadata.FileId == fileId)
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);
                    break;
            }
        }
    }

    private async Task ExecuteWithRetryAsync(
        Func<CancellationToken, Task> operation,
        string description,
        CancellationToken cancellationToken,
        int maxAttempts = 3)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await operation(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                lastError = ex;
                _logger.LogWarning(
                    ex,
                    "{Operation} failed on attempt {Attempt}/{MaxAttempts}, retrying",
                    description,
                    attempt,
                    maxAttempts);
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lastError = ex;
                break;
            }
        }

        throw lastError ?? new InvalidOperationException($"Operation '{description}' failed without emitting an exception.");
    }
}
