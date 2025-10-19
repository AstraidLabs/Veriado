using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Veriado.Appl.Abstractions;
using Veriado.Appl.Common.Exceptions;
using Veriado.Appl.Search;
using Veriado.Domain.Files;
using Veriado.Domain.Search;
using Veriado.Infrastructure.Common;
using Veriado.Infrastructure.Persistence;

namespace Veriado.Infrastructure.Search;

public sealed class SearchProjectionService : IFileSearchProjection
{
    private const string UpsertWithGuardSql = @"INSERT INTO search_document (
    file_id,
    title,
    author,
    mime,
    metadata_text,
    metadata_json,
    created_utc,
    modified_utc,
    content_hash,
    stored_content_hash,
    stored_token_hash)
VALUES (
    $file_id,
    $title,
    $author,
    $mime,
    $metadata_text,
    $metadata_json,
    $created_utc,
    $modified_utc,
    $content_hash,
    $stored_content_hash,
    $stored_token_hash)
ON CONFLICT(file_id) DO UPDATE SET
    title = excluded.title,
    author = excluded.author,
    mime = excluded.mime,
    metadata_text = excluded.metadata_text,
    metadata_json = excluded.metadata_json,
    created_utc = excluded.created_utc,
    modified_utc = excluded.modified_utc,
    content_hash = excluded.content_hash,
    stored_content_hash = excluded.stored_content_hash,
    stored_token_hash = excluded.stored_token_hash
WHERE (search_document.stored_content_hash IS NULL OR search_document.stored_content_hash = $expected_old_hash)
  AND (search_document.stored_token_hash IS NULL OR search_document.stored_token_hash = $expected_old_token_hash);";

    private const string UpsertWithoutGuardSql = @"INSERT INTO search_document (
    file_id,
    title,
    author,
    mime,
    metadata_text,
    metadata_json,
    created_utc,
    modified_utc,
    content_hash,
    stored_content_hash,
    stored_token_hash)
VALUES (
    $file_id,
    $title,
    $author,
    $mime,
    $metadata_text,
    $metadata_json,
    $created_utc,
    $modified_utc,
    $content_hash,
    $stored_content_hash,
    $stored_token_hash)
ON CONFLICT(file_id) DO UPDATE SET
    title = excluded.title,
    author = excluded.author,
    mime = excluded.mime,
    metadata_text = excluded.metadata_text,
    metadata_json = excluded.metadata_json,
    created_utc = excluded.created_utc,
    modified_utc = excluded.modified_utc,
    content_hash = excluded.content_hash,
    stored_content_hash = excluded.stored_content_hash,
    stored_token_hash = excluded.stored_token_hash;";

    private readonly DbContext _dbContext;
    private readonly IAnalyzerFactory _analyzerFactory;
    private readonly ILogger<SearchProjectionService> _logger;
    private readonly ISearchTelemetry? _telemetry;

    public SearchProjectionService(
        DbContext dbContext,
        IAnalyzerFactory analyzerFactory,
        ILogger<SearchProjectionService>? logger = null,
        ISearchTelemetry? telemetry = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
        _logger = logger ?? NullLogger<SearchProjectionService>.Instance;
        _telemetry = telemetry;
    }

    public async Task UpsertAsync(
        FileEntity file,
        string? expectedContentHash,
        string? expectedTokenHash,
        string? newContentHash,
        string? tokenHash,
        ISearchProjectionTransactionGuard guard,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(guard);

        guard.EnsureActiveTransaction(_dbContext);

        if (!SqliteFulltextSupport.IsAvailable)
        {
            return;
        }

        var document = file.ToSearchDocument();
        var normalizedTitle = NormalizeOptional(document.Title);
        var normalizedAuthor = NormalizeOptional(document.Author);
        var normalizedMetadataText = NormalizeOptional(document.MetadataText);

        var expectedHash = string.IsNullOrWhiteSpace(expectedContentHash)
            ? null
            : expectedContentHash;
        var expectedToken = string.IsNullOrWhiteSpace(expectedTokenHash)
            ? null
            : expectedTokenHash;
        var storedContentHash = string.IsNullOrWhiteSpace(newContentHash)
            ? (string?)document.ContentHash
            : newContentHash;
        var storedTokenHash = string.IsNullOrWhiteSpace(tokenHash)
            ? null
            : tokenHash;

        var contentHashValue = string.IsNullOrWhiteSpace(document.ContentHash)
            ? storedContentHash
            : document.ContentHash;

        var rowsAffected = await SqliteRetry.ExecuteAsync(
                () => ExecuteUpsertCoreAsync(
                    document,
                    normalizedTitle,
                    normalizedAuthor,
                    normalizedMetadataText,
                    expectedHash,
                    expectedToken,
                    storedContentHash,
                    storedTokenHash,
                    applyGuard: true,
                    cancellationToken),
                (exception, attempt, delay) =>
                {
                    _telemetry?.RecordSqliteBusyRetry(1);
                    _logger.LogWarning(
                        exception,
                        "SQLite busy while updating search document for {FileId}; retrying in {Delay} (attempt {Attempt}/5)",
                        document.FileId,
                        delay,
                        attempt);
                    return Task.CompletedTask;
                },
                (exception, attempt) =>
                {
                    _telemetry?.RecordSqliteBusyRetry(1);
                    _logger.LogError(
                        exception,
                        "SQLite busy while updating search document for {FileId} after {Attempts} attempts; aborting.",
                        document.FileId,
                        attempt);
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (rowsAffected == 0)
        {
            var current = await ReadStoredProjectionAsync(document.FileId, cancellationToken).ConfigureAwait(false);

            if (current is not null
                && EqualsOrdinal(current.StoredContentHash, storedContentHash)
                && EqualsOrdinal(current.StoredTokenHash, storedTokenHash)
                && EqualsOrdinal(current.ContentHash, contentHashValue)
                && EqualsOrdinal(current.Title, normalizedTitle)
                && EqualsOrdinal(current.Author, normalizedAuthor)
                && EqualsOrdinal(current.MetadataText, normalizedMetadataText)
                && EqualsOrdinal(current.MetadataJson, document.MetadataJson)
                && EqualsOrdinal(current.Mime, document.Mime)
                && current.CreatedUtc == document.CreatedUtc
                && current.ModifiedUtc == document.ModifiedUtc)
            {
                throw new AnalyzerOrContentDriftException();
            }

            throw new StaleSearchProjectionUpdateException();
        }
    }

    public async Task ForceReplaceAsync(
        FileEntity file,
        string? newContentHash,
        string? tokenHash,
        ISearchProjectionTransactionGuard guard,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(guard);

        guard.EnsureActiveTransaction(_dbContext);

        if (!SqliteFulltextSupport.IsAvailable)
        {
            return;
        }

        var document = file.ToSearchDocument();
        var normalizedTitle = NormalizeOptional(document.Title);
        var normalizedAuthor = NormalizeOptional(document.Author);
        var normalizedMetadataText = NormalizeOptional(document.MetadataText);

        var storedContentHash = string.IsNullOrWhiteSpace(newContentHash)
            ? (string?)document.ContentHash
            : newContentHash;
        var storedTokenHash = string.IsNullOrWhiteSpace(tokenHash)
            ? null
            : tokenHash;

        await SqliteRetry.ExecuteAsync(
                () => ExecuteUpsertCoreAsync(
                    document,
                    normalizedTitle,
                    normalizedAuthor,
                    normalizedMetadataText,
                    expectedContentHash: null,
                    expectedTokenHash: null,
                    storedContentHash,
                    storedTokenHash,
                    applyGuard: false,
                    cancellationToken),
                (exception, attempt, delay) =>
                {
                    _telemetry?.RecordSqliteBusyRetry(1);
                    _logger.LogWarning(
                        exception,
                        "SQLite busy while force updating search document for {FileId}; retrying in {Delay} (attempt {Attempt}/5)",
                        document.FileId,
                        delay,
                        attempt);
                    return Task.CompletedTask;
                },
                (exception, attempt) =>
                {
                    _telemetry?.RecordSqliteBusyRetry(1);
                    _logger.LogError(
                        exception,
                        "SQLite busy while force updating search document for {FileId} after {Attempts} attempts; aborting.",
                        document.FileId,
                        attempt);
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<int> ExecuteUpsertCoreAsync(
        SearchDocument document,
        string? normalizedTitle,
        string? normalizedAuthor,
        string? normalizedMetadataText,
        string? expectedContentHash,
        string? expectedTokenHash,
        string? storedContentHash,
        string? storedTokenHash,
        bool applyGuard,
        CancellationToken cancellationToken)
    {
        var sqliteTransaction = GetAmbientTransaction();
        var connection = sqliteTransaction.Connection ?? throw new InvalidOperationException(
            "Active SQLite transaction has no associated connection.");

        await using var command = connection.CreateCommand();
        command.Transaction = sqliteTransaction;
        command.CommandText = applyGuard ? UpsertWithGuardSql : UpsertWithoutGuardSql;

        var contentHashValue = string.IsNullOrWhiteSpace(document.ContentHash)
            ? storedContentHash
            : document.ContentHash;

        ConfigureSearchDocumentParameters(
            command,
            document.FileId,
            normalizedTitle,
            normalizedAuthor,
            document.Mime,
            normalizedMetadataText,
            document.MetadataJson,
            document.CreatedUtc,
            document.ModifiedUtc,
            contentHashValue,
            storedContentHash,
            storedTokenHash,
            expectedContentHash,
            expectedTokenHash);

        try
        {
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            LogSqliteFailure(ex, command);
            if (ex.IndicatesFatalFulltextFailure())
            {
                throw new SearchIndexCorruptedException(
                    "SQLite full-text index became unavailable and needs to be repaired.",
                    ex);
            }

            throw;
        }
    }

    public async Task DeleteAsync(
        Guid fileId,
        ISearchProjectionTransactionGuard guard,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(guard);
        guard.EnsureActiveTransaction(_dbContext);

        if (!SqliteFulltextSupport.IsAvailable)
        {
            return;
        }

        var sqliteTransaction = GetAmbientTransaction();
        var connection = sqliteTransaction.Connection ?? throw new InvalidOperationException(
            "Active SQLite transaction has no associated connection.");

        SqliteCommand? activeCommand = null;

        try
        {
            await using var delete = connection.CreateCommand();
            delete.Transaction = sqliteTransaction;
            delete.CommandText = "DELETE FROM search_document WHERE file_id = $file_id;";
            delete.Parameters.Add("$file_id", SqliteType.Blob).Value = fileId.ToByteArray();
            activeCommand = delete;
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            activeCommand = null;
        }
        catch (SqliteException ex)
        {
            LogSqliteFailure(ex, activeCommand);
            if (ex.IndicatesFatalFulltextFailure())
            {
                throw new SearchIndexCorruptedException(
                    "SQLite full-text index became unavailable and needs to be repaired.",
                    ex);
            }

            throw;
        }
    }

    private SqliteTransaction GetAmbientTransaction()
    {
        var transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Search projection operations require an active EF Core transaction.");

        if (transaction is not SqliteTransaction sqliteTransaction)
        {
            throw new InvalidOperationException("Search projection operations require a SQLite transaction.");
        }

        return sqliteTransaction;
    }

    private string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : TextNormalization.NormalizeText(value, _analyzerFactory);
    }

    private static void ConfigureSearchDocumentParameters(
        SqliteCommand command,
        Guid fileId,
        string? normalizedTitle,
        string? normalizedAuthor,
        string mime,
        string? normalizedMetadataText,
        string? metadataJson,
        DateTimeOffset createdUtc,
        DateTimeOffset modifiedUtc,
        string? contentHash,
        string? storedContentHash,
        string? storedTokenHash,
        string? expectedOldHash,
        string? expectedOldTokenHash)
    {
        if (string.IsNullOrEmpty(mime))
        {
            throw new ArgumentException("MIME type is required for search_document writes.", nameof(mime));
        }

        command.Parameters.Clear();
        command.Parameters.Add("$file_id", SqliteType.Blob).Value = fileId.ToByteArray();
        command.Parameters.Add("$title", SqliteType.Text).Value = (object?)normalizedTitle ?? DBNull.Value;
        command.Parameters.Add("$author", SqliteType.Text).Value = (object?)normalizedAuthor ?? DBNull.Value;
        command.Parameters.Add("$mime", SqliteType.Text).Value = mime;
        command.Parameters.Add("$metadata_text", SqliteType.Text).Value = (object?)normalizedMetadataText ?? DBNull.Value;
        command.Parameters.Add("$metadata_json", SqliteType.Text).Value = (object?)metadataJson ?? DBNull.Value;
        command.Parameters.Add("$created_utc", SqliteType.Text).Value = createdUtc.ToString("O", CultureInfo.InvariantCulture);
        command.Parameters.Add("$modified_utc", SqliteType.Text).Value = modifiedUtc.ToString("O", CultureInfo.InvariantCulture);
        command.Parameters.Add("$content_hash", SqliteType.Text).Value = (object?)contentHash ?? DBNull.Value;
        command.Parameters.Add("$stored_content_hash", SqliteType.Text).Value = (object?)storedContentHash ?? DBNull.Value;
        command.Parameters.Add("$stored_token_hash", SqliteType.Text).Value = (object?)storedTokenHash ?? DBNull.Value;
        command.Parameters.Add("$expected_old_hash", SqliteType.Text).Value = (object?)expectedOldHash ?? DBNull.Value;
        command.Parameters.Add("$expected_old_token_hash", SqliteType.Text).Value = (object?)expectedOldTokenHash ?? DBNull.Value;
    }

    private async Task<StoredProjection?> ReadStoredProjectionAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var sqliteTransaction = GetAmbientTransaction();
        var connection = sqliteTransaction.Connection ?? throw new InvalidOperationException(
            "Active SQLite transaction has no associated connection.");

        await using var command = connection.CreateCommand();
        command.Transaction = sqliteTransaction;
        command.CommandText = "SELECT title, author, mime, metadata_text, metadata_json, created_utc, modified_utc, content_hash, stored_content_hash, stored_token_hash FROM search_document WHERE file_id = $file_id LIMIT 1;";
        command.Parameters.Add("$file_id", SqliteType.Blob).Value = fileId.ToByteArray();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var title = reader.IsDBNull(0) ? null : reader.GetString(0);
        var author = reader.IsDBNull(1) ? null : reader.GetString(1);
        var mime = reader.GetString(2);
        var metadataText = reader.IsDBNull(3) ? null : reader.GetString(3);
        var metadataJson = reader.IsDBNull(4) ? null : reader.GetString(4);
        var createdUtc = DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var modifiedUtc = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var contentHash = reader.IsDBNull(7) ? null : reader.GetString(7);
        var storedContentHash = reader.IsDBNull(8) ? null : reader.GetString(8);
        var storedTokenHash = reader.IsDBNull(9) ? null : reader.GetString(9);

        return new StoredProjection(
            title,
            author,
            mime,
            metadataText,
            metadataJson,
            createdUtc,
            modifiedUtc,
            contentHash,
            storedContentHash,
            storedTokenHash);
    }

    private static bool EqualsOrdinal(string? left, string? right)
        => string.Equals(left, right, StringComparison.Ordinal);

    private sealed record StoredProjection(
        string? Title,
        string? Author,
        string Mime,
        string? MetadataText,
        string? MetadataJson,
        DateTimeOffset CreatedUtc,
        DateTimeOffset ModifiedUtc,
        string? ContentHash,
        string? StoredContentHash,
        string? StoredTokenHash);

    private void LogSqliteFailure(SqliteException exception, SqliteCommand? command)
    {
        if (!_logger.IsEnabled(LogLevel.Error))
        {
            return;
        }

        var builder = new StringBuilder();

        if (command is not null)
        {
            builder.AppendLine("CommandText:");
            builder.AppendLine(command.CommandText);
            builder.AppendLine("Parameters:");

            foreach (SqliteParameter parameter in command.Parameters)
            {
                builder
                    .Append("  ")
                    .Append(parameter.ParameterName)
                    .Append(" = ")
                    .Append(FormatParameterValue(parameter.Value))
                    .AppendLine();
            }
        }

        var snapshot = SqliteFulltextSupport.SchemaSnapshot;
        var mode = snapshot?.IsContentless switch
        {
            true => "contentless",
            false => "content-linked",
            _ => "unknown",
        };

        var triggerSummary = snapshot?.HasSearchDocumentTriggers == true
            ? string.Join(", ", snapshot.Triggers.Keys)
            : "missing";

        builder.Append("FTS schema mode=")
            .Append(mode)
            .Append(", triggers=")
            .Append(triggerSummary)
            .Append(", lastChecked=")
            .Append(snapshot?.CheckedAtUtc.ToString("O", CultureInfo.InvariantCulture) ?? "<never>");

        var reason = SqliteFulltextSupport.FailureReason;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            builder.Append(", failureReason=").Append(reason);
        }

        _logger.LogError(exception, "SQLite FTS command failed. {Details}", builder.ToString());
    }

    private static string FormatParameterValue(object? value)
    {
        return value switch
        {
            null or DBNull => "NULL",
            byte[] blob => $"BLOB(length={blob.Length})",
            string text when text.Length > 256 => $"TEXT(len={text.Length})",
            string text => text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "<unrepresentable>",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "<unrepresentable>",
        };
    }
}
