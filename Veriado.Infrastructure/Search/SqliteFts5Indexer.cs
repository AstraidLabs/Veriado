using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Veriado.Application.Abstractions;
using Veriado.Domain.Search;
using Veriado.Infrastructure.Persistence.Options;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides thread-safe access to the SQLite FTS5 search index.
/// </summary>
internal sealed class SqliteFts5Indexer : ISearchIndexer, ISearchQueryService
{
    private readonly InfrastructureOptions _options;
    public SqliteFts5Indexer(InfrastructureOptions options)
    {
        _options = options;
    }

    public async Task IndexAsync(SearchDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var helper = new SqliteFts5Transactional();
        await helper.IndexAsync(document, connection, (SqliteTransaction)transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var helper = new SqliteFts5Transactional();
        await helper.DeleteAsync(fileId, connection, (SqliteTransaction)transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SearchDocument>> SearchAsync(string query, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (limit <= 0)
        {
            limit = 10;
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT m.file_id, s.title, s.mime, s.author, f.created_utc, f.modified_utc, snippet(s, 3, '[', ']', '…', 10) as snippet " +
            "FROM file_search s " +
            "JOIN file_search_map m ON s.rowid = m.rowid " +
            "JOIN files f ON f.id = m.file_id " +
            "WHERE file_search MATCH $query " +
            "ORDER BY rank " +
            "LIMIT $limit;";
        command.Parameters.AddWithValue("$query", query);
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<SearchDocument>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var fileIdBlob = (byte[])reader[0];
            var fileId = new Guid(fileIdBlob);
            var title = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var mime = reader.IsDBNull(2) ? "application/octet-stream" : reader.GetString(2);
            var author = reader.IsDBNull(3) ? null : reader.GetString(3);
            var created = reader.GetString(4);
            var modified = reader.GetString(5);
            var snippet = reader.IsDBNull(6) ? null : reader.GetString(6);

            var createdUtc = DateTimeOffset.Parse(created, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
            var modifiedUtc = DateTimeOffset.Parse(modified, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
            var document = new SearchDocument(fileId, title, mime, author, createdUtc, modifiedUtc, snippet);
            results.Add(document);
        }

        return results;
    }

    private SqliteConnection CreateConnection()
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("Infrastructure has not been initialised with a connection string.");
        }

        return new SqliteConnection(_options.ConnectionString);
    }
}
