using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veriado.Application.Abstractions;
using Veriado.Domain.Files;
using Veriado.Domain.Primitives;
using Veriado.Domain.Search.Events;
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

    public WriteWorker(
        IWriteQueue writeQueue,
        IDbContextFactory<AppDbContext> dbContextFactory,
        ILogger<WriteWorker> logger,
        InfrastructureOptions options,
        ITextExtractor textExtractor,
        IEventPublisher eventPublisher)
    {
        _writeQueue = writeQueue;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
        _options = options;
        _textExtractor = textExtractor;
        _eventPublisher = eventPublisher;
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

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (_options.FtsIndexingMode == FtsIndexingMode.SameTransaction)
        {
            await ApplyFulltextUpdatesAsync(context, filesToIndex, filesToDelete, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        foreach (var entry in fileEntries)
        {
            entry.Entity.ClearDomainEvents();
        }

        for (var index = 0; index < batch.Count; index++)
        {
            batch[index].TrySetResult(results[index]);
        }

        foreach (var domainEvent in domainEvents)
        {
            try
            {
                await _eventPublisher.PublishAsync(domainEvent, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Domain event publication failed for {EventType}", domainEvent.GetType().Name);
            }
        }
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
            await helper.DeleteAsync(id, sqliteConnection, sqliteTransaction, cancellationToken).ConfigureAwait(false);
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var file in filesToIndex)
        {
            var text = await _textExtractor.ExtractAsync(file, cancellationToken).ConfigureAwait(false);
            var document = file.ToSearchDocument(text);
            await helper.IndexAsync(document, sqliteConnection, sqliteTransaction, cancellationToken).ConfigureAwait(false);
            file.ConfirmIndexed(file.SearchIndex.SchemaVersion, now);
        }

        if (filesToIndex.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
