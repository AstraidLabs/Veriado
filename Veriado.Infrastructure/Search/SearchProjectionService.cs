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
using Veriado.Infrastructure.Persistence;

namespace Veriado.Infrastructure.Search;

public sealed class SearchProjectionService : IFileSearchProjection
{
    private const int SqliteBusyErrorCode = 5;
    private const int MaxBusyRetries = 5;
    private const double InitialBackoffMilliseconds = 25d;
    private const double MaxBackoffMilliseconds = 400d;

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
        var storedContentHash = string.IsNullOrWhiteSpace(newContentHash)
            ? (string?)document.ContentHash
            : newContentHash;
        var storedTokenHash = string.IsNullOrWhiteSpace(tokenHash)
            ? null
            : tokenHash;

        var attempt = 0;
        var delay = TimeSpan.FromMilliseconds(InitialBackoffMilliseconds);

        while (true)
        {
            attempt++;

            try
            {
                var rowsAffected = await ExecuteUpsertCoreAsync(
                        document,
                        normalizedTitle,
                        normalizedAuthor,
                        normalizedMetadataText,
                        expectedHash,
                        storedContentHash,
                        storedTokenHash,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (rowsAffected == 0)
                {
                    throw new StaleSearchProjectionUpdateException();
                }

                break;
            }
            catch (SqliteException ex) when (IsBusy(ex) && attempt < MaxBusyRetries)
            {
                _telemetry?.RecordSqliteBusyRetry(1);
                _logger.LogWarning(
                    ex,
                    "SQLite busy while updating search document for {FileId}; retrying in {Delay} (attempt {Attempt}/{MaxAttempts})",
                    document.FileId,
                    delay,
                    attempt,
                    MaxBusyRetries);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                var nextDelay = Math.Min(delay.TotalMilliseconds * 2, MaxBackoffMilliseconds);
                delay = TimeSpan.FromMilliseconds(nextDelay);
            }
            catch (SqliteException ex) when (IsBusy(ex))
            {
                _telemetry?.RecordSqliteBusyRetry(1);
                _logger.LogError(
                    ex,
                    "SQLite busy while updating search document for {FileId} after {Attempts} attempts; aborting.",
                    document.FileId,
                    attempt);
                throw;
            }
        }
    }

    private async Task<int> ExecuteUpsertCoreAsync(
        SearchDocument document,
        string? normalizedTitle,
        string? normalizedAuthor,
        string? normalizedMetadataText,
        string? expectedContentHash,
        string? storedContentHash,
        string? storedTokenHash,
        CancellationToken cancellationToken)
    {
        var sqliteTransaction = GetAmbientTransaction();
        var connection = sqliteTransaction.Connection ?? throw new InvalidOperationException(
            "Active SQLite transaction has no associated connection.");

        await using var command = connection.CreateCommand();
        command.Transaction = sqliteTransaction;
        command.CommandText = @"INSERT INTO search_document (
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
WHERE search_document.stored_content_hash IS NULL
   OR search_document.stored_content_hash = $expected_old_hash;";

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
            expectedContentHash);

        try
        {
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex) when (IsBusy(ex))
        {
            throw;
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

    private static bool IsBusy(SqliteException exception)
        => exception.SqliteErrorCode == SqliteBusyErrorCode;

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
        string? expectedOldHash)
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
    }

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
