using System;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Hosting;
using Veriado.Domain.Primitives;
using Veriado.Domain.Search.Events;
using Veriado.Infrastructure.Search;
using Veriado.Appl.Search;

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
    private readonly ISearchIndexCoordinator _searchCoordinator;
    private readonly ISearchIndexer _searchIndexer;
    private readonly IFulltextIntegrityService _integrityService;
    private readonly IEventPublisher _eventPublisher;
    private readonly IClock _clock;
    private readonly IAnalyzerFactory _analyzerFactory;

    public WriteWorker(
        IWriteQueue writeQueue,
        IDbContextFactory<AppDbContext> dbContextFactory,
        ILogger<WriteWorker> logger,
        InfrastructureOptions options,
        ISearchIndexCoordinator searchCoordinator,
        ISearchIndexer searchIndexer,
        IFulltextIntegrityService integrityService,
        IEventPublisher eventPublisher,
        IClock clock,
        IAnalyzerFactory analyzerFactory)
    {
        _writeQueue = writeQueue;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
        _options = options;
        _searchCoordinator = searchCoordinator;
        _searchIndexer = searchIndexer;
        _integrityService = integrityService;
        _eventPublisher = eventPublisher;
        _clock = clock;
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Write worker started");
        Exception? terminationException = null;
        try
        {
            while (true)
            {
                stoppingToken.ThrowIfCancellationRequested();
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
        catch (OperationCanceledException oce)
        {
            terminationException = oce;
            _logger.LogInformation("Write worker stopping");
        }
        catch (Exception ex)
        {
            terminationException = ex;
            _logger.LogError(ex, "Write worker terminated unexpectedly");
            throw;
        }
        finally
        {
            _writeQueue.Complete(terminationException);
            DrainPendingRequests(terminationException, stoppingToken);
        }
    }

    private void DrainPendingRequests(Exception? terminationException, CancellationToken stoppingToken)
    {
        while (_writeQueue.TryDequeue(out var pending))
        {
            if (pending is null)
            {
                continue;
            }

            if (terminationException is OperationCanceledException || stoppingToken.IsCancellationRequested)
            {
                pending.TrySetCanceled();
                continue;
            }

            if (terminationException is not null)
            {
                pending.TrySetException(terminationException);
                continue;
            }

            pending.TrySetException(new InvalidOperationException("Write worker stopped before processing the request."));
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
        var repairAttempted = false;

        while (true)
        {
            try
            {
                await ProcessBatchAttemptAsync(batch, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (SearchIndexCorruptedException ex)
            {
                if (repairAttempted)
                {
                    _logger.LogCritical(
                        ex,
                        "Full-text search index corruption persists after automatic repair. Manual intervention is required.");
                    throw;
                }

                repairAttempted = true;
                _logger.LogWarning(
                    ex,
                    "Full-text search index corruption detected while processing a write batch. Initiating automatic repair.");

                await AttemptIntegrityRepairAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Retrying write batch after repairing the full-text search index.");
            }
        }
    }

    private async Task ProcessBatchAttemptAsync(IReadOnlyList<WriteRequest> batch, CancellationToken cancellationToken)
    {
        var batchStopwatch = Stopwatch.StartNew();
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var results = new object?[batch.Count];
        Exception? failure = null;
        var trackedOptions = new Dictionary<FileEntity, FilePersistenceOptions>();
        foreach (var request in batch)
        {
            if (request.TrackedFiles is { Count: > 0 } trackedFiles)
            {
                foreach (var tracked in trackedFiles)
                {
                    trackedOptions[tracked.Entity] = tracked.Options;
                }
            }
        }

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
        var filesToIndex = new List<(FileEntity File, FilePersistenceOptions Options)>();
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
                    var options = trackedOptions.TryGetValue(entry.Entity, out var value)
                        ? value
                        : FilePersistenceOptions.Default;
                    filesToIndex.Add((entry.Entity, options));
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

        if (_options.FtsIndexingMode == FtsIndexingMode.Outbox && filesToIndex.Count > 0)
        {
            var reindexByFile = reindexEvents
                .GroupBy(evt => evt.FileId)
                .ToDictionary(group => group.Key, group => group.OrderBy(evt => evt.OccurredOnUtc).Last());

            foreach (var (file, options) in filesToIndex)
            {
                if (!options.AllowDeferredIndexing)
                {
                    continue;
                }

                reindexByFile.TryGetValue(file.Id, out var reindexEvent);
                var occurredUtc = reindexEvent?.OccurredOnUtc ?? _clock.UtcNow;
                var reason = reindexEvent?.Reason.ToString() ?? ReindexReason.Manual.ToString();
                var outbox = OutboxEvent.From(nameof(SearchReindexRequested), new
                {
                    FileId = file.Id,
                    Reason = reason,
                }, occurredUtc);
                context.OutboxEvents.Add(outbox);
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var indexedNow = await ApplyFulltextUpdatesAsync(context, filesToIndex, filesToDelete, cancellationToken)
            .ConfigureAwait(false);

        if (indexedNow)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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

    private async Task<bool> ApplyFulltextUpdatesAsync(
        AppDbContext context,
        IReadOnlyList<(FileEntity File, FilePersistenceOptions Options)> filesToIndex,
        IReadOnlyList<Guid> filesToDelete,
        CancellationToken cancellationToken)
    {
        if (filesToIndex.Count == 0 && filesToDelete.Count == 0)
        {
            return false;
        }

        if (!_options.IsFulltextAvailable)
        {
            if (filesToIndex.Count == 0)
            {
                return false;
            }

            var timestamp = UtcTimestamp.From(_clock.UtcNow);
            foreach (var (file, _) in filesToIndex)
            {
                file.ConfirmIndexed(file.SearchIndex.SchemaVersion, timestamp);
            }

            return true;
        }

        var handled = false;
        var dbTransaction = context.Database.CurrentTransaction?.GetDbTransaction();

        if (_options.FtsIndexingMode == FtsIndexingMode.SameTransaction)
        {
            if (dbTransaction is not SqliteTransaction sqliteTransaction)
            {
                throw new InvalidOperationException("SQLite transaction is required for full-text updates.");
            }

            var sqliteConnection = (SqliteConnection)sqliteTransaction.Connection!;
            var helper = new SqliteFts5Transactional(_analyzerFactory);

            foreach (var id in filesToDelete)
            {
                await ExecuteWithRetryAsync(
                    ct => helper.DeleteAsync(id, sqliteConnection, sqliteTransaction, beforeCommit: null, ct),
                    $"delete index for {id}",
                    cancellationToken).ConfigureAwait(false);
            }

            foreach (var (file, options) in filesToIndex)
            {
                var indexed = false;
                await ExecuteWithRetryAsync(
                    async ct =>
                    {
                        indexed = await _searchCoordinator
                            .IndexAsync(file, options, sqliteTransaction, ct)
                            .ConfigureAwait(false);
                    },
                    $"index file {file.Id}",
                    cancellationToken).ConfigureAwait(false);
                if (indexed)
                {
                    var timestamp = UtcTimestamp.From(_clock.UtcNow);
                    file.ConfirmIndexed(file.SearchIndex.SchemaVersion, timestamp);
                    handled = true;
                }
            }

            return handled;
        }

        foreach (var id in filesToDelete)
        {
            await _searchIndexer.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        }

        foreach (var (file, options) in filesToIndex)
        {
            var indexed = await _searchCoordinator.IndexAsync(file, options, dbTransaction, cancellationToken)
                .ConfigureAwait(false);
            if (indexed)
            {
                var timestamp = UtcTimestamp.From(_clock.UtcNow);
                file.ConfirmIndexed(file.SearchIndex.SchemaVersion, timestamp);
                handled = true;
            }
        }

        return handled;
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
            catch (SearchIndexCorruptedException)
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

    private async Task AttemptIntegrityRepairAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogWarning("Attempting automatic full rebuild of the full-text search index due to detected corruption.");
            var repaired = await _integrityService.RepairAsync(reindexAll: true, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation("Automatic full-text index repair completed ({Repaired} entries updated).", repaired);
        }
        catch (Exception repairEx)
        {
            _logger.LogCritical(repairEx, "Automatic full-text index repair failed. Manual intervention is required.");
            throw;
        }
    }
}
