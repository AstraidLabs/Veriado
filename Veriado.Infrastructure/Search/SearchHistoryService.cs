using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Veriado.Application.Search.Abstractions;
using Veriado.Infrastructure.Persistence.Options;

namespace Veriado.Infrastructure.Search;

internal sealed class SearchHistoryService : ISearchHistoryService
{
    private readonly InfrastructureOptions _options;

    public SearchHistoryService(InfrastructureOptions options)
    {
        _options = options;
    }

    public async Task AddAsync(string? queryText, string matchQuery, int totalCount, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matchQuery);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow.ToString("O");

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = (SqliteTransaction)transaction;
            update.CommandText =
                "UPDATE search_history " +
                "SET query_text = $queryText, created_utc = $createdUtc, executions = executions + 1, last_total_hits = $hits " +
                "WHERE match = $match;";
            update.Parameters.Add("$queryText", SqliteType.Text).Value = (object?)queryText ?? DBNull.Value;
            update.Parameters.Add("$createdUtc", SqliteType.Text).Value = now;
            update.Parameters.Add("$hits", SqliteType.Integer).Value = totalCount;
            update.Parameters.Add("$match", SqliteType.Text).Value = matchQuery;
            var affected = await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (affected == 0)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = (SqliteTransaction)transaction;
                insert.CommandText =
                    "INSERT INTO search_history(id, query_text, match, created_utc, executions, last_total_hits) " +
                    "VALUES ($id, $queryText, $match, $createdUtc, 1, $hits);";
                insert.Parameters.Add("$id", SqliteType.Blob).Value = Guid.NewGuid().ToByteArray();
                insert.Parameters.Add("$queryText", SqliteType.Text).Value = (object?)queryText ?? DBNull.Value;
                insert.Parameters.Add("$match", SqliteType.Text).Value = matchQuery;
                insert.Parameters.Add("$createdUtc", SqliteType.Text).Value = now;
                insert.Parameters.Add("$hits", SqliteType.Integer).Value = totalCount;
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SearchHistoryEntry>> GetRecentAsync(int take, CancellationToken cancellationToken)
    {
        if (take <= 0)
        {
            take = 10;
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, query_text, match, created_utc, executions, last_total_hits " +
            "FROM search_history " +
            "ORDER BY created_utc DESC, id DESC " +
            "LIMIT $limit;";
        command.Parameters.Add("$limit", SqliteType.Integer).Value = take;

        var entries = new List<SearchHistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = new Guid((byte[])reader[0]);
            var text = reader.IsDBNull(1) ? null : reader.GetString(1);
            var match = reader.GetString(2);
            var created = reader.GetString(3);
            var createdUtc = DateTimeOffset.Parse(created, null, DateTimeStyles.RoundtripKind);
            var executions = reader.GetInt32(4);
            var lastTotal = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);
            entries.Add(new SearchHistoryEntry(id, text, match, createdUtc, executions, lastTotal));
        }

        return entries;
    }

    public async Task ClearAsync(int? keepLastN, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        if (!keepLastN.HasValue || keepLastN.Value <= 0)
        {
            await using var deleteAll = connection.CreateCommand();
            deleteAll.CommandText = "DELETE FROM search_history;";
            await deleteAll.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var delete = connection.CreateCommand();
        delete.CommandText =
            "DELETE FROM search_history WHERE id NOT IN (" +
            "    SELECT id FROM search_history ORDER BY created_utc DESC, id DESC LIMIT $keep" +
            ");";
        delete.Parameters.Add("$keep", SqliteType.Integer).Value = keepLastN.Value;
        await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
