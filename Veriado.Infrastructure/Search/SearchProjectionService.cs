using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Veriado.Appl.Abstractions;
using Veriado.Appl.Search;
using Veriado.Domain.Files;
using Veriado.Infrastructure.Persistence;

namespace Veriado.Infrastructure.Search;

public sealed class SearchProjectionService : IFileSearchProjection
{
    private readonly DbContext _dbContext;
    private readonly IAnalyzerFactory _analyzerFactory;
    private readonly ILogger<SearchProjectionService> _logger;

    public SearchProjectionService(
        DbContext dbContext,
        IAnalyzerFactory analyzerFactory,
        ILogger<SearchProjectionService>? logger = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
        _logger = logger ?? NullLogger<SearchProjectionService>.Instance;
    }

    public async Task UpsertAsync(
        FileEntity file,
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
        var sqliteTransaction = GetAmbientTransaction();
        var connection = sqliteTransaction.Connection ?? throw new InvalidOperationException(
            "Active SQLite transaction has no associated connection.");

        SqliteCommand? activeCommand = null;

        try
        {
            var normalizedTitle = NormalizeOptional(document.Title);
            var normalizedAuthor = NormalizeOptional(document.Author);
            var normalizedMetadataText = NormalizeOptional(document.MetadataText);

            await using var update = connection.CreateCommand();
            update.Transaction = sqliteTransaction;
            update.CommandText = @"UPDATE search_document
SET title = $title,
    author = $author,
    mime = $mime,
    metadata_text = $metadata_text,
    metadata_json = $metadata_json,
    created_utc = $created_utc,
    modified_utc = $modified_utc,
    content_hash = $content_hash
WHERE file_id = $file_id;";

            ConfigureSearchDocumentParameters(
                update,
                document.FileId,
                normalizedTitle,
                normalizedAuthor,
                document.Mime,
                normalizedMetadataText,
                document.MetadataJson,
                document.CreatedUtc,
                document.ModifiedUtc,
                document.ContentHash);

            activeCommand = update;
            var rowsAffected = await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            activeCommand = null;

            if (rowsAffected == 0)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = sqliteTransaction;
                insert.CommandText = @"INSERT INTO search_document (
    file_id,
    title,
    author,
    mime,
    metadata_text,
    metadata_json,
    created_utc,
    modified_utc,
    content_hash)
VALUES (
    $file_id,
    $title,
    $author,
    $mime,
    $metadata_text,
    $metadata_json,
    $created_utc,
    $modified_utc,
    $content_hash);";

                ConfigureSearchDocumentParameters(
                    insert,
                    document.FileId,
                    normalizedTitle,
                    normalizedAuthor,
                    document.Mime,
                    normalizedMetadataText,
                    document.MetadataJson,
                    document.CreatedUtc,
                    document.ModifiedUtc,
                    document.ContentHash);

                activeCommand = insert;
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                activeCommand = null;
            }
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
        string? contentHash)
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
