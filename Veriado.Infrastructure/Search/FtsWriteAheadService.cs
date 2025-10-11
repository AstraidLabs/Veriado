using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Veriado.Infrastructure.Persistence;
using Veriado.Appl.Search;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides write-ahead journalling support for FTS5 operations, including crash recovery on startup.
/// </summary>
internal sealed class FtsWriteAheadService
{
    public const string OperationIndex = "index";
    public const string OperationDelete = "delete";

    private static readonly AsyncLocal<int> SuppressionCounter = new();

    private readonly ILogger<FtsWriteAheadService> _logger;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IAnalyzerFactory _analyzerFactory;
    private readonly TrigramIndexOptions _trigramOptions;

    public FtsWriteAheadService(
        ILogger<FtsWriteAheadService> logger,
        IDbContextFactory<AppDbContext> dbContextFactory,
        ISqliteConnectionFactory connectionFactory,
        IAnalyzerFactory analyzerFactory,
        TrigramIndexOptions trigramOptions)
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
        _trigramOptions = trigramOptions ?? throw new ArgumentNullException(nameof(trigramOptions));
    }

    public bool IsLoggingEnabled => SuppressionCounter.Value == 0;

    public IDisposable SuppressLogging()
    {
        SuppressionCounter.Value += 1;
        return new Scope(static () => SuppressionCounter.Value -= 1);
    }

    public async Task<long?> LogAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid fileId,
        string operation,
        string? contentHash,
        string? normalizedTitle,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrEmpty(operation);
        if (!IsLoggingEnabled)
        {
            return null;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO fts_write_ahead (file_id, op, content_hash, title_hash, enqueued_utc)
            VALUES ($file_id, $op, $content_hash, $title_hash, $enqueued_utc)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$file_id", fileId.ToString("D"));
        command.Parameters.AddWithValue("$op", operation);
        command.Parameters.AddWithValue("$content_hash", (object?)contentHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$title_hash", (object?)ComputeTitleHash(normalizedTitle) ?? DBNull.Value);
        command.Parameters.AddWithValue("$enqueued_utc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is long id ? id : null;
    }

    public async Task ClearAsync(SqliteConnection connection, SqliteTransaction? transaction, long id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM fts_write_ahead WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReplayPendingAsync(CancellationToken cancellationToken)
    {
        await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = lease.Connection;
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken: cancellationToken).ConfigureAwait(false);

        var pending = await LoadPendingAsync(connection, cancellationToken).ConfigureAwait(false);
        if (pending.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Replaying {Count} pending FTS write-ahead entries", pending.Count);
        var helper = new SqliteFts5Transactional(_analyzerFactory, _trigramOptions, this);

        foreach (var entry in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(entry.FileId, out var fileId))
            {
                _logger.LogWarning(
                    "Unable to parse file identifier '{FileId}' for FTS write-ahead entry {EntryId}. Moving to DLQ.",
                    entry.FileId,
                    entry.Id);
                await MoveToDeadLetterAsync(connection, entry, "Invalid file identifier", cancellationToken).ConfigureAwait(false);
                continue;
            }

            switch (entry.Operation)
            {
                case OperationIndex:
                    await ReplayIndexAsync(connection, helper, entry, fileId, cancellationToken).ConfigureAwait(false);
                    break;
                case OperationDelete:
                    await ReplayDeleteAsync(connection, helper, entry, fileId, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    _logger.LogWarning(
                        "Unknown FTS write-ahead operation '{Operation}' for entry {EntryId}. Moving to DLQ.",
                        entry.Operation,
                        entry.Id);
                    await MoveToDeadLetterAsync(connection, entry, $"Unknown operation '{entry.Operation}'", cancellationToken)
                        .ConfigureAwait(false);
                    break;
            }
        }
    }

    public async Task MoveToDeadLetterAsync(SqliteConnection connection, FtsWriteAheadEntry entry, string error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await MoveToDeadLetterInternalAsync(connection, transaction, entry, error, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ReplayIndexAsync(
        SqliteConnection connection,
        SqliteFts5Transactional helper,
        FtsWriteAheadEntry entry,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var file = await context.Files
            .AsNoTracking()
            .Include(f => f.Content)
            .FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken)
            .ConfigureAwait(false);

        if (file is null)
        {
            _logger.LogWarning(
                "Skipping FTS write-ahead entry {EntryId} because file {FileId} no longer exists.",
                entry.Id,
                entry.FileId);
            await MoveToDeadLetterAsync(connection, entry, "File no longer exists", cancellationToken).ConfigureAwait(false);
            return;
        }

        var document = file.ToSearchDocument();

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var scope = SuppressLogging();
            await helper.IndexAsync(document, connection, transaction, beforeCommit: null, cancellationToken, enlistJournal: false)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            await ClearAsync(connection, transaction: null, entry.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError(
                ex,
                "Failed to replay FTS index entry {EntryId} for file {FileId}. Moving to DLQ.",
                entry.Id,
                entry.FileId);
            await MoveToDeadLetterAsync(connection, entry, ex.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReplayDeleteAsync(
        SqliteConnection connection,
        SqliteFts5Transactional helper,
        FtsWriteAheadEntry entry,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var scope = SuppressLogging();
            await helper.DeleteAsync(fileId, connection, transaction, beforeCommit: null, cancellationToken, enlistJournal: false)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            await ClearAsync(connection, transaction: null, entry.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError(
                ex,
                "Failed to replay FTS delete entry {EntryId} for file {FileId}. Moving to DLQ.",
                entry.Id,
                entry.FileId);
            await MoveToDeadLetterAsync(connection, entry, ex.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<List<FtsWriteAheadEntry>> LoadPendingAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var entries = new List<FtsWriteAheadEntry>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, file_id, op, content_hash, title_hash, enqueued_utc FROM fts_write_ahead ORDER BY id;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetInt64(0);
            var fileId = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var operation = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var contentHash = reader.IsDBNull(3) ? null : reader.GetString(3);
            var titleHash = reader.IsDBNull(4) ? null : reader.GetString(4);
            var enqueuedText = reader.IsDBNull(5) ? null : reader.GetString(5);
            var enqueuedUtc = DateTimeOffset.TryParse(enqueuedText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : DateTimeOffset.MinValue;
            entries.Add(new FtsWriteAheadEntry(id, fileId, operation, contentHash, titleHash, enqueuedUtc));
        }

        return entries;
    }

    private async Task MoveToDeadLetterInternalAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FtsWriteAheadEntry entry,
        string error,
        CancellationToken cancellationToken)
    {
        using var scope = SuppressLogging();
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO fts_write_ahead_dlq (
                original_id,
                file_id,
                op,
                content_hash,
                title_hash,
                enqueued_utc,
                dead_lettered_utc,
                error)
            VALUES ($original_id, $file_id, $op, $content_hash, $title_hash, $enqueued_utc, $dead_lettered_utc, $error);
            """;
        insert.Parameters.AddWithValue("$original_id", entry.Id);
        insert.Parameters.AddWithValue("$file_id", entry.FileId);
        insert.Parameters.AddWithValue("$op", entry.Operation);
        insert.Parameters.AddWithValue("$content_hash", (object?)entry.ContentHash ?? DBNull.Value);
        insert.Parameters.AddWithValue("$title_hash", (object?)entry.TitleHash ?? DBNull.Value);
        insert.Parameters.AddWithValue("$enqueued_utc", entry.EnqueuedUtc.ToString("O", CultureInfo.InvariantCulture));
        insert.Parameters.AddWithValue("$dead_lettered_utc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        insert.Parameters.AddWithValue("$error", error);
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM fts_write_ahead WHERE id = $id;";
        delete.Parameters.AddWithValue("$id", entry.Id);
        await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string? ComputeTitleHash(string? normalizedTitle)
    {
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return null;
        }

        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(normalizedTitle);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private readonly record struct Scope(Action DisposeAction) : IDisposable
    {
        public void Dispose() => DisposeAction();
    }
}

internal readonly record struct FtsWriteAheadEntry(
    long Id,
    string FileId,
    string Operation,
    string? ContentHash,
    string? TitleHash,
    DateTimeOffset EnqueuedUtc);
