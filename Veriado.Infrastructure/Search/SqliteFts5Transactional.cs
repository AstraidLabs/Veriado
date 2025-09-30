using System;
using System.Collections.Generic;
using System.Linq;
using Veriado.Appl.Search;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides helper methods for manipulating the SQLite FTS5 tables within an ambient transaction.
/// </summary>
internal sealed class SqliteFts5Transactional
{
    private readonly IAnalyzerFactory _analyzerFactory;
    private readonly TrigramIndexOptions _trigramOptions;

    public SqliteFts5Transactional(IAnalyzerFactory analyzerFactory, TrigramIndexOptions trigramOptions)
    {
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
        _trigramOptions = trigramOptions ?? throw new ArgumentNullException(nameof(trigramOptions));
    }

    public async Task IndexAsync(
        SearchDocument document,
        SqliteConnection connection,
        SqliteTransaction transaction,
        Func<CancellationToken, Task>? beforeCommit,
        CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(document);
            if (!SqliteFulltextSupport.IsAvailable)
            {
                return;
            }
            var searchRowId = await EnsureMapRowIdAsync("file_search_map", document.FileId, connection, transaction, cancellationToken).ConfigureAwait(false);
            var trigramRowId = await EnsureMapRowIdAsync("file_trgm_map", document.FileId, connection, transaction, cancellationToken).ConfigureAwait(false);

            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "INSERT INTO file_search(file_search, rowid) VALUES ('delete', $rowid);";
                delete.Parameters.Add("$rowid", SqliteType.Integer).Value = searchRowId;
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var normalizedTitle = NormalizeOptional(document.Title);
            var normalizedAuthor = NormalizeOptional(document.Author);
            var normalizedMetadataText = NormalizeOptional(document.MetadataText);

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

            var trigramSegments = BuildTrigramSegments(document);

            var trigramText = trigramSegments.Count == 0
                ? string.Empty
                : TrigramQueryBuilder.BuildIndexEntry(trigramSegments.ToArray());

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

            if (beforeCommit is not null)
            {
                await beforeCommit(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (SqliteException ex) when (ex.IndicatesDatabaseCorruption() || ex.IndicatesFulltextSchemaMissing())
        {
            throw new SearchIndexCorruptedException("SQLite full-text index is corrupted and needs to be repaired.", ex);
        }
    }

    public async Task DeleteAsync(
        Guid fileId,
        SqliteConnection connection,
        SqliteTransaction transaction,
        Func<CancellationToken, Task>? beforeCommit,
        CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(transaction);
            if (!SqliteFulltextSupport.IsAvailable)
            {
                return;
            }
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

            if (beforeCommit is not null)
            {
                await beforeCommit(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (SqliteException ex) when (ex.IndicatesDatabaseCorruption() || ex.IndicatesFulltextSchemaMissing())
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

    private List<string> BuildTrigramSegments(SearchDocument document)
    {
        var segments = new List<string>();
        var maxTokens = Math.Max(1, _trigramOptions.MaxTokens);
        var totalTokens = 0;

        foreach (var candidate in EnumerateTrigramValues(document))
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var tokens = TextNormalization
                .Tokenize(candidate, _analyzerFactory)
                .Where(static token => !string.IsNullOrWhiteSpace(token))
                .ToArray();
            if (tokens.Length == 0)
            {
                continue;
            }

            var remaining = maxTokens - totalTokens;
            if (remaining <= 0)
            {
                break;
            }

            if (tokens.Length > remaining)
            {
                tokens = tokens.Take(remaining).ToArray();
            }

            segments.Add(string.Join(' ', tokens));
            totalTokens += tokens.Length;

            if (totalTokens >= maxTokens)
            {
                break;
            }
        }

        return segments;
    }

    private IEnumerable<string?> EnumerateTrigramValues(SearchDocument document)
    {
        if (_trigramOptions.Fields is null || _trigramOptions.Fields.Length == 0)
        {
            yield break;
        }

        foreach (var field in _trigramOptions.Fields)
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                continue;
            }

            switch (field.Trim().ToLowerInvariant())
            {
                case "title":
                    yield return document.Title;
                    break;
                case "author":
                    yield return document.Author;
                    break;
                case "filename":
                    yield return document.FileName;
                    break;
                case "metadata_text":
                    yield return document.MetadataText;
                    break;
            }
        }
    }
}
