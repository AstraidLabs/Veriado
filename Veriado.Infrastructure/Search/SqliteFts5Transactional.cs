using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Veriado.Domain.Search;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides helper methods for manipulating the SQLite FTS5 tables within an ambient transaction.
/// </summary>
internal sealed class SqliteFts5Transactional
{
    public async Task<long> EnsureRowIdAsync(Guid fileId, SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        var fileKey = fileId.ToByteArray();

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO file_search_map(file_id) VALUES ($fileId) ON CONFLICT(file_id) DO NOTHING;";
            insert.Parameters.Add("$fileId", SqliteType.Blob).Value = fileKey;
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = "SELECT rowid FROM file_search_map WHERE file_id = $fileId;";
        select.Parameters.Add("$fileId", SqliteType.Blob).Value = fileKey;
        var rowId = await select.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (rowId is long value)
        {
            return value;
        }

        throw new InvalidOperationException("Failed to resolve FTS row identifier.");
    }

    public async Task IndexAsync(SearchDocument document, SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var rowId = await EnsureRowIdAsync(document.FileId, connection, transaction, cancellationToken).ConfigureAwait(false);

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM file_search WHERE rowid = $rowid;";
            delete.Parameters.AddWithValue("$rowid", rowId);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO file_search(rowid, title, mime, author, content) VALUES ($rowid, $title, $mime, $author, $content);";
            insert.Parameters.AddWithValue("$rowid", rowId);
            insert.Parameters.AddWithValue("$title", (object?)document.Title ?? DBNull.Value);
            insert.Parameters.AddWithValue("$mime", document.Mime);
            insert.Parameters.AddWithValue("$author", (object?)document.Author ?? DBNull.Value);
            insert.Parameters.AddWithValue("$content", (object?)document.ContentText ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task DeleteAsync(Guid fileId, SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        var fileKey = fileId.ToByteArray();
        long? rowId = null;

        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT rowid FROM file_search_map WHERE file_id = $fileId;";
            select.Parameters.Add("$fileId", SqliteType.Blob).Value = fileKey;
            var result = await select.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (result is long value)
            {
                rowId = value;
            }
        }

        if (rowId.HasValue)
        {
            await using var deleteFts = connection.CreateCommand();
            deleteFts.Transaction = transaction;
            deleteFts.CommandText = "DELETE FROM file_search WHERE rowid = $rowid;";
            deleteFts.Parameters.AddWithValue("$rowid", rowId.Value);
            await deleteFts.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var deleteMap = connection.CreateCommand())
        {
            deleteMap.Transaction = transaction;
            deleteMap.CommandText = "DELETE FROM file_search_map WHERE file_id = $fileId;";
            deleteMap.Parameters.Add("$fileId", SqliteType.Blob).Value = fileKey;
            await deleteMap.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
