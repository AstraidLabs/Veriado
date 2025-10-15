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

            await using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = @"UPDATE DocumentContent
SET Title = $title,
    Author = $author,
    Mime = $mime,
    MetadataText = $metadata_text,
    Metadata = $metadata
WHERE FileId = $fileId;";
                ConfigureDocumentContentParameters(
                    update,
                    document.FileId,
                    normalizedTitle,
                    normalizedAuthor,
                    document.Mime,
                    normalizedMetadataText,
                    document.MetadataJson);

                activeCommand = update;
                var affected = await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                if (affected == 0)
                {
                    await using var insert = connection.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = @"INSERT INTO DocumentContent (FileId, Title, Author, Mime, MetadataText, Metadata)
VALUES ($fileId, $title, $author, $mime, $metadata_text, $metadata);";
                    ConfigureDocumentContentParameters(
                        insert,
                        document.FileId,
                        normalizedTitle,
                        normalizedAuthor,
                        document.Mime,
                        normalizedMetadataText,
                        document.MetadataJson);

                    activeCommand = insert;
                    await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
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

            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM DocumentContent WHERE FileId = $fileId;";
                delete.Parameters.Add("$fileId", SqliteType.Blob).Value = fileId.ToByteArray();
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

    private static void ConfigureDocumentContentParameters(
        SqliteCommand command,
        Guid fileId,
        string? normalizedTitle,
        string? normalizedAuthor,
        string mime,
        string? normalizedMetadataText,
        string? metadataJson)
    {
        command.Parameters.Add("$fileId", SqliteType.Blob).Value = fileId.ToByteArray();
        command.Parameters.AddWithValue("$title", (object?)normalizedTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$author", (object?)normalizedAuthor ?? DBNull.Value);
        command.Parameters.AddWithValue("$mime", mime);
        command.Parameters.AddWithValue("$metadata_text", (object?)normalizedMetadataText ?? DBNull.Value);
        command.Parameters.AddWithValue("$metadata", (object?)metadataJson ?? DBNull.Value);
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
