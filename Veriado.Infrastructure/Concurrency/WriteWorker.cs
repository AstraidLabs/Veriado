using System;
using System.Diagnostics;
using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
    #region TODO(SQLiteOnly): Streamline dependencies after removing deferred/outbox pipeline
    private readonly ISearchIndexCoordinator _searchCoordinator;
    private readonly ISearchIndexer _searchIndexer;
    private readonly IFulltextIntegrityService _integrityService;
    private readonly IEventPublisher _eventPublisher;
    private readonly IClock _clock;
    private readonly IAnalyzerFactory _analyzerFactory;
    private readonly TrigramIndexOptions _trigramOptions;
    private readonly INeedsReindexEvaluator _needsReindexEvaluator;
    private readonly ISearchIndexSignatureCalculator _signatureCalculator;
    private readonly FtsWriteAheadService _writeAhead;
    #endregion

    private static readonly FilePersistenceOptions SameTransactionOptions = new()
    {
        AllowDeferredIndexing = false,
    };

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
        IAnalyzerFactory analyzerFactory,
        TrigramIndexOptions trigramOptions,
        INeedsReindexEvaluator needsReindexEvaluator,
        ISearchIndexSignatureCalculator signatureCalculator,
        FtsWriteAheadService writeAhead)
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
        _trigramOptions = trigramOptions ?? throw new ArgumentNullException(nameof(trigramOptions));
        _needsReindexEvaluator = needsReindexEvaluator ?? throw new ArgumentNullException(nameof(needsReindexEvaluator));
        _signatureCalculator = signatureCalculator ?? throw new ArgumentNullException(nameof(signatureCalculator));
        _writeAhead = writeAhead ?? throw new ArgumentNullException(nameof(writeAhead));
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

        if (context.Database.GetDbConnection() is not SqliteConnection sqliteConnection)
        {
            throw new InvalidOperationException("SQLite connection is required for write operations.");
        }

        if (sqliteConnection.State != ConnectionState.Open)
        {
            await sqliteConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var sqliteTransaction = await sqliteConnection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var transaction = await context.Database
            .UseTransactionAsync(sqliteTransaction, cancellationToken)
            .ConfigureAwait(false);

        var transactionId = Guid.NewGuid();

        _logger.LogInformation(
            "Write transaction {TransactionId} started for batch size {BatchSize}",
            transactionId,
            batch.Count);

        var results = new object?[batch.Count];
        Exception? failure = null;
        var trackedOptions = new Dictionary<FileEntity, FilePersistenceOptions>();
        foreach (var request in batch)
        {
            if (request.TrackedFiles is { Count: > 0 } trackedFiles)
            {
                foreach (var tracked in trackedFiles)
                {
                    trackedOptions[tracked.Entity] = NormalizePersistenceOptions(tracked.Entity, tracked.Options);
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
            _logger.LogWarning(
                failure,
                "Write transaction {TransactionId} rolling back due to {ErrorType}",
                transactionId,
                failure.GetType().Name);

            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            foreach (var request in batch)
            {
                request.TrySetException(failure);
            }

            return;
        }

        var fileEntries = context.ChangeTracker.Entries<FileEntity>().ToList();

        foreach (var entry in fileEntries)
        {
            if (entry.State == EntityState.Deleted)
            {
                continue;
            }

            var file = entry.Entity;
            var searchIndex = file.SearchIndex ?? throw new InvalidOperationException("File is missing search index state.");

            if (!searchIndex.IsStale)
            {
                var needsReindex = await _needsReindexEvaluator
                    .NeedsReindexAsync(file, searchIndex, cancellationToken)
                    .ConfigureAwait(false);
                if (needsReindex)
                {
                    searchIndex.MarkStale();
                }
            }
        }

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
                        : SameTransactionOptions;
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

        try
        {
            var indexedNow = await ApplyFulltextUpdatesAsync(
                    context,
                    sqliteTransaction,
                    transactionId,
                    filesToIndex,
                    filesToDelete,
                    cancellationToken)
                .ConfigureAwait(false);

            _logger.LogDebug(
                indexedNow
                    ? "Full-text updates applied for transaction {TransactionId}"
                    : "No full-text updates required for transaction {TransactionId}",
                transactionId);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SearchIndexCorruptedException)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        if (failure is not null)
        {
            _logger.LogWarning(
                failure,
                "Write transaction {TransactionId} rolling back due to {ErrorType}",
                transactionId,
                failure.GetType().Name);

            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            foreach (var request in batch)
            {
                request.TrySetException(failure);
            }

            return;
        }

        try
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        if (failure is not null)
        {
            _logger.LogWarning(
                failure,
                "Write transaction {TransactionId} rolling back due to {ErrorType}",
                transactionId,
                failure.GetType().Name);

            try
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError(
                    rollbackEx,
                    "Failed to rollback write transaction {TransactionId} after commit error.",
                    transactionId);
            }

            foreach (var request in batch)
            {
                request.TrySetException(failure);
            }

            return;
        }

        batchStopwatch.Stop();

        _logger.LogInformation(
            "Write transaction {TransactionId} committed",
            transactionId);

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
            "Processed write batch of {BatchSize} items in {ElapsedMilliseconds} ms (indexed {IndexedCount}) [Transaction: {TransactionId}]",
            batch.Count,
            batchStopwatch.Elapsed.TotalMilliseconds,
            staleCount,
            transactionId);
    }

    private FilePersistenceOptions NormalizePersistenceOptions(FileEntity file, FilePersistenceOptions requestedOptions)
    {
        if (requestedOptions.AllowDeferredIndexing)
        {
            _logger.LogWarning(
                "Deferred indexing requested for file {FileId}, but SameTransaction mode is enforced. Processing inline instead.",
                file.Id);
        }

        return SameTransactionOptions;
    }

    private async Task<bool> ApplyFulltextUpdatesAsync(
        AppDbContext context,
        SqliteTransaction sqliteTransaction,
        Guid transactionId,
        IReadOnlyList<(FileEntity File, FilePersistenceOptions Options)> filesToIndex,
        IReadOnlyList<Guid> filesToDelete,
        CancellationToken cancellationToken)
    {
        if (filesToIndex.Count == 0 && filesToDelete.Count == 0)
        {
            return false;
        }

        var sqliteConnection = sqliteTransaction.Connection
            ?? throw new InvalidOperationException("SQLite connection is unavailable for the active transaction.");

        if (!_options.IsFulltextAvailable)
        {
            if (filesToIndex.Count == 0)
            {
                return false;
            }

            var timestamp = UtcTimestamp.From(_clock.UtcNow);
            foreach (var (file, _) in filesToIndex)
            {
                var signature = _signatureCalculator.Compute(file);
                file.ConfirmIndexed(
                    file.SearchIndex.SchemaVersion,
                    timestamp,
                    signature.AnalyzerVersion,
                    signature.TokenHash,
                    signature.NormalizedTitle);
            }

            return true;
        }

        var helper = new SqliteFts5Transactional(_analyzerFactory, _trigramOptions, _writeAhead);
        var handled = false;

        foreach (var id in filesToDelete)
        {
            await ExecuteWithRetryAsync(
                async ct =>
                {
                    _logger.LogInformation(
                        "FTS delete for file {FileId} in transaction {TransactionId}",
                        id,
                        transactionId);

                    await helper
                        .DeleteAsync(id, sqliteConnection, sqliteTransaction, beforeCommit: null, ct)
                        .ConfigureAwait(false);
                },
                $"delete index for {id}",
                cancellationToken).ConfigureAwait(false);
        }

        foreach (var (file, options) in filesToIndex)
        {
            var indexed = false;
            await ExecuteWithRetryAsync(
                async ct =>
                {
                    _logger.LogInformation(
                        "FTS upsert for file {FileId} in transaction {TransactionId}",
                        file.Id,
                        transactionId);

                    indexed = await _searchCoordinator
                        .IndexAsync(file, options, sqliteTransaction, ct)
                        .ConfigureAwait(false);
                },
                $"index file {file.Id}",
                cancellationToken).ConfigureAwait(false);

            if (indexed)
            {
                var timestamp = UtcTimestamp.From(_clock.UtcNow);
                var signature = _signatureCalculator.Compute(file);
                file.ConfirmIndexed(
                    file.SearchIndex.SchemaVersion,
                    timestamp,
                    signature.AnalyzerVersion,
                    signature.TokenHash,
                    signature.NormalizedTitle);
                handled = true;
            }
        }

        return handled;
    }

    private async Task ExecuteWithRetryAsync(
        Func<CancellationToken, Task> operation,
        string description,
        CancellationToken cancellationToken,
        int maxAttempts = 5)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var stopwatch = Stopwatch.StartNew();
        Exception? lastError = null;
        var busyRetries = 0;
        var ioErrRetries = 0;
        var otherRetries = 0;
        var attemptsUsed = 0;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            attemptsUsed = attempt;
            try
            {
                await operation(cancellationToken).ConfigureAwait(false);

                stopwatch.Stop();
                if (busyRetries + ioErrRetries + otherRetries > 0)
                {
                    _logger.LogInformation(
                        "{Operation} succeeded after {Attempts} attempts in {ElapsedMilliseconds} ms (busy={BusyRetries}, ioerr={IoErrRetries}, other={OtherRetries})",
                        description,
                        attemptsUsed,
                        stopwatch.Elapsed.TotalMilliseconds,
                        busyRetries,
                        ioErrRetries,
                        otherRetries);
                }

                return;
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                throw;
            }
            catch (SearchIndexCorruptedException)
            {
                stopwatch.Stop();
                throw;
            }
            catch (SqliteException sqliteEx) when (sqliteEx.IndicatesFatalFulltextFailure())
            {
                stopwatch.Stop();
                throw new SearchIndexCorruptedException(
                    "SQLite database corruption detected during full-text operation.",
                    sqliteEx);
            }
            catch (SqliteException sqliteEx) when (attempt < maxAttempts && IsBusySqliteError(sqliteEx))
            {
                busyRetries++;
                lastError = sqliteEx;
                var delay = CalculateBusyBackoff(attempt);
                _logger.LogWarning(
                    sqliteEx,
                    "{Operation} encountered SQLITE_BUSY on attempt {Attempt}/{MaxAttempts}, waiting {Delay} before retry",
                    description,
                    attempt,
                    maxAttempts,
                    delay);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException sqliteEx) when (attempt < maxAttempts && IsIoSqliteError(sqliteEx))
            {
                ioErrRetries++;
                lastError = sqliteEx;
                var delay = CalculateIoBackoff();
                _logger.LogWarning(
                    sqliteEx,
                    "{Operation} encountered SQLITE_IOERR on attempt {Attempt}/{MaxAttempts}, waiting {Delay} before retry",
                    description,
                    attempt,
                    maxAttempts,
                    delay);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException sqliteEx) when (attempt < maxAttempts && IsTransientSqliteError(sqliteEx))
            {
                otherRetries++;
                lastError = sqliteEx;
                var delay = CalculateGeneralBackoff(attempt);
                _logger.LogWarning(
                    sqliteEx,
                    "{Operation} encountered transient SQLite error {ErrorCode} on attempt {Attempt}/{MaxAttempts}, waiting {Delay} before retry",
                    description,
                    sqliteEx.SqliteExtendedErrorCode,
                    attempt,
                    maxAttempts,
                    delay);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                otherRetries++;
                lastError = ex;
                var delay = CalculateGeneralBackoff(attempt);
                _logger.LogWarning(
                    ex,
                    "{Operation} failed on attempt {Attempt}/{MaxAttempts}, waiting {Delay} before retry",
                    description,
                    attempt,
                    maxAttempts,
                    delay);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lastError = ex;
                break;
            }
        }

        stopwatch.Stop();
        if (lastError is not null)
        {
            _logger.LogError(
                lastError,
                "{Operation} failed after {Attempts} attempts in {ElapsedMilliseconds} ms (busy={BusyRetries}, ioerr={IoErrRetries}, other={OtherRetries})",
                description,
                attemptsUsed,
                stopwatch.Elapsed.TotalMilliseconds,
                busyRetries,
                ioErrRetries,
                otherRetries);
        }

        throw lastError ?? new InvalidOperationException($"Operation '{description}' failed without emitting an exception.");
    }

    private static bool IsBusySqliteError(SqliteException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var primary = exception.GetPrimaryErrorCode();
        return primary is 5 or 6;
    }

    private static bool IsIoSqliteError(SqliteException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.GetPrimaryErrorCode() == 10;
    }

    private static bool IsTransientSqliteError(SqliteException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var primary = exception.GetPrimaryErrorCode();
        return primary is 5 or 6 or 10;
    }

    private static TimeSpan CalculateBusyBackoff(int attempt)
    {
        var delayMilliseconds = Math.Min(1000, 50 * Math.Pow(2, attempt - 1));
        return TimeSpan.FromMilliseconds(delayMilliseconds);
    }

    private static TimeSpan CalculateIoBackoff()
    {
        return TimeSpan.FromMilliseconds(250);
    }

    private static TimeSpan CalculateGeneralBackoff(int attempt)
    {
        return TimeSpan.FromMilliseconds(Math.Min(1000, 100 * attempt));
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
