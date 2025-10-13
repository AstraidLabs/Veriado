using System;
using Microsoft.Data.Sqlite;
using Veriado.Appl.Search;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides helper methods for manipulating the SQLite FTS5 tables within an ambient transaction.
/// </summary>
internal sealed class SqliteFts5Transactional
{
    private readonly IAnalyzerFactory _analyzerFactory;
    private readonly FtsWriteAheadService _writeAhead;

    public SqliteFts5Transactional(
        IAnalyzerFactory analyzerFactory,
        FtsWriteAheadService writeAhead)
    {
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
        _writeAhead = writeAhead ?? throw new ArgumentNullException(nameof(writeAhead));
    }

    public async Task<long?> IndexAsync(
        SearchDocument document,
        SqliteConnection connection,
        SqliteTransaction transaction,
        Func<CancellationToken, Task>? beforeCommit,
        CancellationToken cancellationToken,
        bool enlistJournal = true)
    {
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
            await using (var upsert = connection.CreateCommand())
            {
                upsert.Transaction = transaction;
                upsert.CommandText = @"INSERT INTO DocumentContent (FileId, Title, Author, Mime, MetadataText, Metadata)
VALUES ($fileId, $title, $author, $mime, $metadata_text, $metadata)
ON CONFLICT(FileId) DO UPDATE SET
    Title = excluded.Title,
    Author = excluded.Author,
    Mime = excluded.Mime,
    MetadataText = excluded.MetadataText,
    Metadata = excluded.Metadata;";
                upsert.Parameters.Add("$fileId", SqliteType.Blob).Value = document.FileId.ToByteArray();
                upsert.Parameters.AddWithValue("$title", (object?)normalizedTitle ?? DBNull.Value);
                upsert.Parameters.AddWithValue("$author", (object?)normalizedAuthor ?? DBNull.Value);
                upsert.Parameters.AddWithValue("$mime", document.Mime);
                upsert.Parameters.AddWithValue("$metadata_text", (object?)normalizedMetadataText ?? DBNull.Value);
                upsert.Parameters.AddWithValue("$metadata", (object?)document.MetadataJson ?? DBNull.Value);
                await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
        catch (SqliteException ex) when (ex.IndicatesFatalFulltextFailure())
        {
            throw new SearchIndexCorruptedException("SQLite full-text index became unavailable and needs to be repaired.", ex);
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
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
        catch (SqliteException ex) when (ex.IndicatesFatalFulltextFailure())
        {
            throw new SearchIndexCorruptedException("SQLite full-text index became unavailable and needs to be repaired.", ex);
        }
    }

    private string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : TextNormalization.NormalizeText(value, _analyzerFactory);
    }

}
