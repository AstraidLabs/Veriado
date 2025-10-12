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
            var searchRowId = await EnsureMapRowIdAsync("file_search_map", document.FileId, connection, transaction, cancellationToken).ConfigureAwait(false);

            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "INSERT INTO file_search(file_search, rowid) VALUES ('delete', $rowid);";
                delete.Parameters.Add("$rowid", SqliteType.Integer).Value = searchRowId;
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO file_search(rowid, title, mime, author, metadata_text, metadata) VALUES ($rowid, $title, $mime, $author, $metadata_text, $metadata);";
                insert.Parameters.Add("$rowid", SqliteType.Integer).Value = searchRowId;
                insert.Parameters.AddWithValue("$title", (object?)normalizedTitle ?? DBNull.Value);
                insert.Parameters.AddWithValue("$mime", document.Mime);
                insert.Parameters.AddWithValue("$author", (object?)normalizedAuthor ?? DBNull.Value);
                insert.Parameters.AddWithValue("$metadata_text", (object?)normalizedMetadataText ?? DBNull.Value);
                insert.Parameters.AddWithValue("$metadata", (object?)document.MetadataJson ?? DBNull.Value);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
            var fileKey = fileId.ToByteArray();
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

            var searchRowId = await TryGetRowIdAsync("file_search_map", fileId, connection, transaction, cancellationToken).ConfigureAwait(false);
            if (searchRowId.HasValue)
            {
                await using var deleteFts = connection.CreateCommand();
                deleteFts.Transaction = transaction;
                deleteFts.CommandText = "INSERT INTO file_search(file_search, rowid) VALUES ('delete', $rowid);";
                deleteFts.Parameters.Add("$rowid", SqliteType.Integer).Value = searchRowId.Value;
                await deleteFts.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var deleteSearchMap = connection.CreateCommand())
            {
                deleteSearchMap.Transaction = transaction;
                deleteSearchMap.CommandText = "DELETE FROM file_search_map WHERE file_id = $fileId;";
                deleteSearchMap.Parameters.Add("$fileId", SqliteType.Blob).Value = fileKey;
                await deleteSearchMap.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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

    private static async Task<long> EnsureMapRowIdAsync(string mapTable, Guid fileId, SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrEmpty(mapTable);
        var fileKey = fileId.ToByteArray();

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = $"INSERT OR IGNORE INTO {mapTable}(file_id) VALUES ($fileId);";
            insert.Parameters.Add("$fileId", SqliteType.Blob).Value = fileKey;
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = $"SELECT rowid FROM {mapTable} WHERE file_id = $fileId;";
        select.Parameters.Add("$fileId", SqliteType.Blob).Value = fileKey;
        var rowId = await select.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (rowId is long value)
        {
            return value;
        }

        throw new InvalidOperationException($"Failed to resolve row identifier for {mapTable}.");
    }

    private static async Task<long?> TryGetRowIdAsync(string mapTable, Guid fileId, SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrEmpty(mapTable);
        var fileKey = fileId.ToByteArray();

        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = $"SELECT rowid FROM {mapTable} WHERE file_id = $fileId;";
        select.Parameters.Add("$fileId", SqliteType.Blob).Value = fileKey;
        var result = await select.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is long value ? value : null;
    }

    private string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : TextNormalization.NormalizeText(value, _analyzerFactory);
    }

}
