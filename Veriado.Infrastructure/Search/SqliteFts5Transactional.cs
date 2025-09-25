using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Veriado.Appl.Search;
using Veriado.Domain.Search;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides helper methods for manipulating the SQLite FTS5 tables within an ambient transaction.
/// </summary>
internal sealed class SqliteFts5Transactional
{
    public async Task IndexAsync(SearchDocument document, SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(document);
            var searchRowId = await EnsureMapRowIdAsync("file_search_map", document.FileId, connection, transaction, cancellationToken).ConfigureAwait(false);
            var trigramRowId = await EnsureMapRowIdAsync("file_trgm_map", document.FileId, connection, transaction, cancellationToken).ConfigureAwait(false);

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
                insert.CommandText = "INSERT INTO file_search(rowid, title, mime, author, content) VALUES ($rowid, $title, $mime, $author, $content);";
                insert.Parameters.Add("$rowid", SqliteType.Integer).Value = searchRowId;
                insert.Parameters.AddWithValue("$title", (object?)document.Title ?? DBNull.Value);
                insert.Parameters.AddWithValue("$mime", document.Mime);
                insert.Parameters.AddWithValue("$author", (object?)document.Author ?? DBNull.Value);
                insert.Parameters.AddWithValue("$content", (object?)document.ContentText ?? DBNull.Value);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var trigramText = TrigramQueryBuilder.BuildIndexEntry(document.Title, document.Author, document.Subject);

            await using (var deleteTrgm = connection.CreateCommand())
            {
                deleteTrgm.Transaction = transaction;
                deleteTrgm.CommandText = "INSERT INTO file_trgm(file_trgm, rowid) VALUES ('delete', $rowid);";
                deleteTrgm.Parameters.Add("$rowid", SqliteType.Integer).Value = trigramRowId;
                await deleteTrgm.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var insertTrgm = connection.CreateCommand())
            {
                insertTrgm.Transaction = transaction;
                insertTrgm.CommandText = "INSERT INTO file_trgm(rowid, trgm) VALUES ($rowid, $trgm);";
                insertTrgm.Parameters.Add("$rowid", SqliteType.Integer).Value = trigramRowId;
                insertTrgm.Parameters.AddWithValue("$trgm", trigramText);
                await insertTrgm.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (SqliteException ex) when (ex.IndicatesDatabaseCorruption())
        {
            throw new SearchIndexCorruptedException("SQLite full-text index is corrupted and needs to be repaired.", ex);
        }
    }

    public async Task DeleteAsync(Guid fileId, SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(transaction);
            var fileKey = fileId.ToByteArray();

            var searchRowId = await TryGetRowIdAsync("file_search_map", fileId, connection, transaction, cancellationToken).ConfigureAwait(false);
            if (searchRowId.HasValue)
            {
                await using var deleteFts = connection.CreateCommand();
                deleteFts.Transaction = transaction;
                deleteFts.CommandText = "INSERT INTO file_search(file_search, rowid) VALUES ('delete', $rowid);";
                deleteFts.Parameters.Add("$rowid", SqliteType.Integer).Value = searchRowId.Value;
                await deleteFts.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var trigramRowId = await TryGetRowIdAsync("file_trgm_map", fileId, connection, transaction, cancellationToken).ConfigureAwait(false);
            if (trigramRowId.HasValue)
            {
                await using var deleteTrgm = connection.CreateCommand();
                deleteTrgm.Transaction = transaction;
                deleteTrgm.CommandText = "INSERT INTO file_trgm(file_trgm, rowid) VALUES ('delete', $rowid);";
                deleteTrgm.Parameters.Add("$rowid", SqliteType.Integer).Value = trigramRowId.Value;
                await deleteTrgm.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var deleteSearchMap = connection.CreateCommand())
            {
                deleteSearchMap.Transaction = transaction;
                deleteSearchMap.CommandText = "DELETE FROM file_search_map WHERE file_id = $fileId;";
                deleteSearchMap.Parameters.Add("$fileId", SqliteType.Blob).Value = fileKey;
                await deleteSearchMap.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var deleteTrgmMap = connection.CreateCommand())
            {
                deleteTrgmMap.Transaction = transaction;
                deleteTrgmMap.CommandText = "DELETE FROM file_trgm_map WHERE file_id = $fileId;";
                deleteTrgmMap.Parameters.Add("$fileId", SqliteType.Blob).Value = fileKey;
                await deleteTrgmMap.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (SqliteException ex) when (ex.IndicatesDatabaseCorruption())
        {
            throw new SearchIndexCorruptedException("SQLite full-text index is corrupted and needs to be repaired.", ex);
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
            insert.CommandText = $"INSERT INTO {mapTable}(file_id) VALUES ($fileId) ON CONFLICT(file_id) DO NOTHING;";
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
}
