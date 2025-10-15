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
    private readonly FtsWriteAheadService _writeAhead;
    private readonly ILogger<SqliteFts5Transactional> _logger;

    public SqliteFts5Transactional(
        IAnalyzerFactory analyzerFactory,
        FtsWriteAheadService writeAhead,
        ILogger<SqliteFts5Transactional>? logger = null)
    {
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
        _writeAhead = writeAhead ?? throw new ArgumentNullException(nameof(writeAhead));
        _logger = logger ?? NullLogger<SqliteFts5Transactional>.Instance;
    }

    public async Task<long?> IndexAsync(
        SearchDocument document,
        SqliteConnection connection,
        SqliteTransaction transaction,
        Func<CancellationToken, Task>? beforeCommit,
        CancellationToken cancellationToken,
        bool enlistJournal = true)
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

            var journalTransaction = enlistJournal ? transaction : null;
            var journalId = await _writeAhead
                .LogAsync(
                    connection,
                    journalTransaction,
                    document.FileId,
                    FtsWriteAheadService.OperationIndex,
                    document.ContentHash,
                    normalizedTitle,
                    cancellationToken)
                .ConfigureAwait(false);

            var documentId = await GetDocumentIdAsync(connection, transaction, document.FileId, cancellationToken)
                .ConfigureAwait(false);

            if (documentId.HasValue)
            {
                await using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = @"UPDATE DocumentContent
SET title = $title,
    author = $author,
    mime = $mime,
    metadata_text = $metadata_text,
    metadata = $metadata
WHERE doc_id = $doc_id;";
                ConfigureDocumentContentWriteParameters(
                    update,
                    normalizedTitle,
                    normalizedAuthor,
                    document.Mime,
                    normalizedMetadataText,
                    document.MetadataJson);
                update.Parameters.Add("$doc_id", SqliteType.Integer).Value = documentId.Value;

                activeCommand = update;
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                activeCommand = null;
            }
            else
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = @"INSERT INTO DocumentContent (file_id, title, author, mime, metadata_text, metadata)
VALUES ($file_id, $title, $author, $mime, $metadata_text, $metadata);";
                ConfigureDocumentContentInsertParameters(
                    insert,
                    document.FileId,
                    normalizedTitle,
                    normalizedAuthor,
                    document.Mime,
                    normalizedMetadataText,
                    document.MetadataJson);

                activeCommand = insert;
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                activeCommand = null;
            }

            if (enlistJournal && journalId.HasValue)
            {
                await _writeAhead.ClearAsync(connection, transaction, journalId.Value, cancellationToken).ConfigureAwait(false);
                journalId = null;
            }

            if (beforeCommit is not null)
            {
                await beforeCommit(cancellationToken).ConfigureAwait(false);
            }

            return enlistJournal ? null : journalId;
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
        CancellationToken cancellationToken,
        bool enlistJournal = true)
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
            var journalTransaction = enlistJournal ? transaction : null;
            var journalId = await _writeAhead
                .LogAsync(
                    connection,
                    journalTransaction,
                    fileId,
                    FtsWriteAheadService.OperationDelete,
                    contentHash: null,
                    normalizedTitle: null,
                    cancellationToken)
                .ConfigureAwait(false);

            var documentId = await GetDocumentIdAsync(connection, transaction, fileId, cancellationToken)
                .ConfigureAwait(false);
            if (documentId.HasValue)
            {
                await using var delete = connection.CreateCommand();
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM DocumentContent WHERE doc_id = $doc_id;";
                delete.Parameters.Add("$doc_id", SqliteType.Integer).Value = documentId.Value;
                activeCommand = delete;
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                activeCommand = null;
            }

            if (enlistJournal && journalId.HasValue)
            {
                await _writeAhead.ClearAsync(connection, transaction, journalId.Value, cancellationToken).ConfigureAwait(false);
                journalId = null;
            }

            if (beforeCommit is not null)
            {
                await beforeCommit(cancellationToken).ConfigureAwait(false);
            }

            return enlistJournal ? null : journalId;
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

    private static Task<long?> GetDocumentIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        return GetDocumentIdCoreAsync(connection, transaction, fileId, cancellationToken);
    }

    private static async Task<long?> GetDocumentIdCoreAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT doc_id FROM DocumentContent WHERE file_id = $file_id;";
        command.Parameters.Add("$file_id", SqliteType.Blob).Value = fileId.ToByteArray();
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result switch
        {
            long value => value,
            int value => value,
            _ => null,
        };
    }

    private static void ConfigureDocumentContentInsertParameters(
        SqliteCommand command,
        Guid fileId,
        string? normalizedTitle,
        string? normalizedAuthor,
        string mime,
        string? normalizedMetadataText,
        string? metadataJson)
    {
        command.Parameters.Add("$file_id", SqliteType.Blob).Value = fileId.ToByteArray();
        ConfigureDocumentContentWriteParameters(
            command,
            normalizedTitle,
            normalizedAuthor,
            mime,
            normalizedMetadataText,
            metadataJson);
    }

    private static void ConfigureDocumentContentWriteParameters(
        SqliteCommand command,
        string? normalizedTitle,
        string? normalizedAuthor,
        string mime,
        string? normalizedMetadataText,
        string? metadataJson)
    {
        if (string.IsNullOrEmpty(mime))
        {
            throw new ArgumentException("MIME type is required for DocumentContent writes.", nameof(mime));
        }
        command.Parameters.Add("$title", SqliteType.Text).Value = (object?)normalizedTitle ?? DBNull.Value;
        command.Parameters.Add("$author", SqliteType.Text).Value = (object?)normalizedAuthor ?? DBNull.Value;
        command.Parameters.Add("$mime", SqliteType.Text).Value = mime;
        command.Parameters.Add("$metadata_text", SqliteType.Text).Value = (object?)normalizedMetadataText ?? DBNull.Value;
        command.Parameters.Add("$metadata", SqliteType.Text).Value = (object?)metadataJson ?? DBNull.Value;
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

        var triggerSummary = snapshot?.HasDocumentContentTriggers == true
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
