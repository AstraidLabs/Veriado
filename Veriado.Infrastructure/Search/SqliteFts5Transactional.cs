using System;
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Veriado.Appl.Search;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides helper methods for manipulating the SQLite FTS5 tables within an ambient transaction.
/// </summary>
internal sealed class SqliteFts5Transactional
{
    private readonly IAnalyzerFactory _analyzerFactory;
    private readonly ILogger<SqliteFts5Transactional> _logger;

    public SqliteFts5Transactional(
        IAnalyzerFactory analyzerFactory,
        ILogger<SqliteFts5Transactional>? logger = null)
    {
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
        _logger = logger ?? NullLogger<SqliteFts5Transactional>.Instance;
    }

    public async Task<long?> IndexAsync(
        SearchDocument document,
        SqliteConnection connection,
        SqliteTransaction transaction,
        Func<CancellationToken, Task>? beforeCommit,
        CancellationToken cancellationToken)
    {
        SqliteCommand? activeCommand = null;
        try
        {
            ArgumentNullException.ThrowIfNull(document);
            if (!SqliteFulltextSupport.IsAvailable)
            {
                return null;
            }

            var normalizedTitle = NormalizeOptional(document.Title);
            var normalizedAuthor = NormalizeOptional(document.Author);
            var normalizedMetadataText = NormalizeOptional(document.MetadataText);

            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
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
                insert.Transaction = transaction;
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

            if (beforeCommit is not null)
            {
                await beforeCommit(cancellationToken).ConfigureAwait(false);
            }

            return null;
        }
        catch (SqliteException ex)
        {
            LogSqliteFailure(ex, activeCommand);
            if (ex.IndicatesFatalFulltextFailure())
            {
                throw new SearchIndexCorruptedException("SQLite full-text index became unavailable and needs to be repaired.", ex);
            }

            throw;
        }
    }

    public async Task<long?> DeleteAsync(
        Guid fileId,
        SqliteConnection connection,
        SqliteTransaction transaction,
        Func<CancellationToken, Task>? beforeCommit,
        CancellationToken cancellationToken)
    {
        SqliteCommand? activeCommand = null;
        try
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(transaction);
            if (!SqliteFulltextSupport.IsAvailable)
            {
                return null;
            }
            await using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM search_document WHERE file_id = $file_id;";
            delete.Parameters.Add("$file_id", SqliteType.Blob).Value = fileId.ToByteArray();
            activeCommand = delete;
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            activeCommand = null;

            if (beforeCommit is not null)
            {
                await beforeCommit(cancellationToken).ConfigureAwait(false);
            }

            return null;
        }
        catch (SqliteException ex)
        {
            LogSqliteFailure(ex, activeCommand);
            if (ex.IndicatesFatalFulltextFailure())
            {
                throw new SearchIndexCorruptedException("SQLite full-text index became unavailable and needs to be repaired.", ex);
            }

            throw;
        }
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

        _logger.LogError(
            exception,
            "SQLite FTS command failed. {Details}",
            builder.ToString());
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
