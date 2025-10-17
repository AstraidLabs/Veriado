using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veriado.Appl.Abstractions;
using Veriado.Appl.Search;
using Veriado.Domain.ValueObjects;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.EventLog;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Processes the <c>reindex_queue</c> table and rebuilds search documents in an idempotent manner.
/// </summary>
internal sealed class ReindexWorker : BackgroundService
{
    private const int DefaultBatchSize = 32;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(30);

    private readonly InfrastructureOptions _options;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly SqliteFts5Transactional _ftsTransactional;
    private readonly ISearchIndexSignatureCalculator _signatureCalculator;
    private readonly IClock _clock;
    private readonly ILogger<ReindexWorker> _logger;

    public ReindexWorker(
        InfrastructureOptions options,
        IDbContextFactory<AppDbContext> dbContextFactory,
        IAnalyzerFactory analyzerFactory,
        FtsWriteAheadService writeAhead,
        ILogger<SqliteFts5Transactional> ftsLogger,
        ISearchIndexSignatureCalculator signatureCalculator,
        IClock clock,
        ILogger<ReindexWorker> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        ArgumentNullException.ThrowIfNull(analyzerFactory);
        ArgumentNullException.ThrowIfNull(writeAhead);
        _signatureCalculator = signatureCalculator ?? throw new ArgumentNullException(nameof(signatureCalculator));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _ftsTransactional = new SqliteFts5Transactional(analyzerFactory, writeAhead, ftsLogger);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Reindex worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pending = await FetchPendingEntriesAsync(stoppingToken).ConfigureAwait(false);
                if (pending.Count == 0)
                {
                    await Task.Delay(IdleDelay, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                foreach (var entryId in pending)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    await ProcessEntryAsync(entryId, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SearchIndexCorruptedException ex)
            {
                _logger.LogCritical(ex, "Reindex worker detected search index corruption. Stopping worker.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected failure inside reindex worker loop. Pausing before retry.");
                try
                {
                    await Task.Delay(ErrorDelay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("Reindex worker stopped.");
    }

    private async Task<IReadOnlyList<long>> FetchPendingEntriesAsync(CancellationToken cancellationToken)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.ReindexQueue
            .Where(entry => entry.ProcessedUtc == null)
            .OrderBy(entry => entry.Id)
            .Select(entry => entry.Id)
            .Take(DefaultBatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ProcessEntryAsync(long entryId, CancellationToken cancellationToken)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        if (context.Database.GetDbConnection() is not SqliteConnection sqliteConnection)
        {
            throw new InvalidOperationException("Reindex worker requires a SQLite connection.");
        }

        if (sqliteConnection.State != ConnectionState.Open)
        {
            await sqliteConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var sqliteTransaction = (SqliteTransaction)await sqliteConnection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var _ = await context.Database
            .UseTransactionAsync(sqliteTransaction, cancellationToken)
            .ConfigureAwait(false);

        ReindexQueueEntry? entry = null;
        var attemptStartedUtc = _clock.UtcNow;

        try
        {
            entry = await context.ReindexQueue
                .FirstOrDefaultAsync(item => item.Id == entryId, cancellationToken)
                .ConfigureAwait(false);

            if (entry is null)
            {
                await sqliteTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (entry.ProcessedUtc.HasValue)
            {
                await sqliteTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            var file = await context.Files
                .Include(file => file.SearchIndex)
                .FirstOrDefaultAsync(file => file.Id == entry.FileId, cancellationToken)
                .ConfigureAwait(false);

            if (file is null)
            {
                entry.ProcessedUtc = attemptStartedUtc;
                entry.RetryCount = 0;
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await sqliteTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("Reindex entry {EntryId} skipped because file {FileId} no longer exists.", entry.Id, entry.FileId);
                return;
            }

            if (_options.IsFulltextAvailable)
            {
                var document = file.ToSearchDocument();
                await _ftsTransactional
                    .IndexAsync(document, sqliteConnection, sqliteTransaction, beforeCommit: null, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                _logger.LogDebug("FTS unavailable; confirming indexing for file {FileId} without touching search_document.", entry.FileId);
            }

            var signature = _signatureCalculator.Compute(file);
            file.ConfirmIndexed(
                file.SearchIndex?.SchemaVersion ?? 1,
                UtcTimestamp.From(attemptStartedUtc),
                signature.AnalyzerVersion,
                signature.TokenHash,
                signature.NormalizedTitle);

            entry.ProcessedUtc = attemptStartedUtc;
            entry.RetryCount = 0;

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await sqliteTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Processed reindex entry {EntryId} for file {FileId}.",
                entry.Id,
                entry.FileId);
        }
        catch (SearchIndexCorruptedException)
        {
            await sqliteTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException)
        {
            await sqliteTransaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (IsRetriable(ex))
        {
            await sqliteTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            var retryCount = await IncrementRetryCountAsync(entryId, cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(
                ex,
                "Transient failure processing reindex entry {EntryId} for file {FileId}. Retry count is now {RetryCount}.",
                entry?.Id ?? entryId,
                entry?.FileId,
                retryCount);
        }
        catch (Exception ex)
        {
            await sqliteTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            await MarkProcessedWithErrorAsync(entryId, attemptStartedUtc, cancellationToken).ConfigureAwait(false);
            _logger.LogError(
                ex,
                "Failed to process reindex entry {EntryId} for file {FileId}. Marked as processed to avoid retry loops.",
                entry?.Id ?? entryId,
                entry?.FileId);
        }
    }

    private async Task<int> IncrementRetryCountAsync(long entryId, CancellationToken cancellationToken)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entry = await context.ReindexQueue
            .FirstOrDefaultAsync(item => item.Id == entryId, cancellationToken)
            .ConfigureAwait(false);

        if (entry is null || entry.ProcessedUtc.HasValue)
        {
            return 0;
        }

        entry.RetryCount += 1;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return entry.RetryCount;
    }

    private async Task MarkProcessedWithErrorAsync(long entryId, DateTimeOffset processedUtc, CancellationToken cancellationToken)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entry = await context.ReindexQueue
            .FirstOrDefaultAsync(item => item.Id == entryId, cancellationToken)
            .ConfigureAwait(false);

        if (entry is null)
        {
            return;
        }

        if (!entry.ProcessedUtc.HasValue)
        {
            entry.ProcessedUtc = processedUtc;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsRetriable(Exception exception)
    {
        if (exception is SqliteException sqlite)
        {
            var primary = sqlite.GetPrimaryErrorCode();
            return primary is 5 or 6 or 10;
        }

        if (exception is DbUpdateException update && update.InnerException is SqliteException innerSqlite)
        {
            var primary = innerSqlite.GetPrimaryErrorCode();
            return primary is 5 or 6 or 10;
        }

        return false;
    }
}
