using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Veriado.Domain.Search;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides query access to the SQLite FTS5 virtual table.
/// </summary>
internal sealed class SqliteFts5QueryService : ISearchQueryService
{
    private readonly InfrastructureOptions _options;

    public SqliteFts5QueryService(InfrastructureOptions options)
    {
        _options = options;
    }

    public async Task<IReadOnlyList<(Guid Id, double Score)>> SearchWithScoresAsync(
        string matchQuery,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matchQuery);
        if (take <= 0)
        {
            return Array.Empty<(Guid, double)>();
        }

        if (skip < 0)
        {
            skip = 0;
        }

        if (!_options.IsFulltextAvailable)
        {
            return Array.Empty<(Guid, double)>();
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT m.file_id, bm25(s, 4.0, 2.0, 0.5, 1.0) AS score " +
            "FROM file_search s " +
            "JOIN file_search_map m ON s.rowid = m.rowid " +
            "WHERE file_search MATCH $query " +
            "ORDER BY score " +
            "LIMIT $take OFFSET $skip;";
        command.Parameters.Add("$query", SqliteType.Text).Value = matchQuery;
        command.Parameters.Add("$take", SqliteType.Integer).Value = take;
        command.Parameters.Add("$skip", SqliteType.Integer).Value = skip;

        var results = new List<(Guid, double)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var idBytes = (byte[])reader[0];
            var id = new Guid(idBytes);
            var rawScore = reader.IsDBNull(1) ? double.PositiveInfinity : reader.GetDouble(1);
            var score = NormalizeScore(rawScore);
            results.Add((id, score));
        }

        return results;
    }

    public async Task<IReadOnlyList<(Guid Id, double Score)>> SearchFuzzyWithScoresAsync(
        string matchQuery,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matchQuery);
        if (take <= 0)
        {
            return Array.Empty<(Guid, double)>();
        }

        if (skip < 0)
        {
            skip = 0;
        }

        if (!_options.IsFulltextAvailable)
        {
            return Array.Empty<(Guid, double)>();
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT m.file_id, bm25(t) AS score " +
            "FROM file_trgm t " +
            "JOIN file_trgm_map m ON t.rowid = m.rowid " +
            "WHERE file_trgm MATCH $query " +
            "ORDER BY bm25(t) ASC " +
            "LIMIT $take OFFSET $skip;";
        command.Parameters.Add("$query", SqliteType.Text).Value = matchQuery;
        command.Parameters.Add("$take", SqliteType.Integer).Value = take;
        command.Parameters.Add("$skip", SqliteType.Integer).Value = skip;

        var results = new List<(Guid, double)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var idBytes = (byte[])reader[0];
            var id = new Guid(idBytes);
            var rawScore = reader.IsDBNull(1) ? double.PositiveInfinity : reader.GetDouble(1);
            var score = double.IsInfinity(rawScore)
                ? 0d
                : 1d / (1d + Math.Max(0d, rawScore));
            results.Add((id, score));
        }

        return results;
    }

    public async Task<int> CountAsync(string matchQuery, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matchQuery);
        if (!_options.IsFulltextAvailable)
        {
            return 0;
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM file_search WHERE file_search MATCH $query;";
        command.Parameters.Add("$query", SqliteType.Text).Value = matchQuery;

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is long value ? (int)value : 0;
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int? limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var take = limit.GetValueOrDefault(10);
        if (take <= 0)
        {
            take = 10;
        }

        if (!_options.IsFulltextAvailable)
        {
            return Array.Empty<SearchHit>();
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT m.file_id, " +
            "       COALESCE(s.title, ''), " +
            "       COALESCE(s.mime, 'application/octet-stream'), " +
            "       highlight(s, 0, $pre, $post) AS hl_title, " +
            "       snippet(s, 0, $pre, $post, '…', 16) AS snip_title, " +
            "       highlight(s, 1, $pre, $post) AS hl_mime, " +
            "       snippet(s, 1, $pre, $post, '…', 8) AS snip_mime, " +
            "       highlight(s, 2, $pre, $post) AS hl_author, " +
            "       snippet(s, 2, $pre, $post, '…', 8) AS snip_author, " +
            "       highlight(s, 3, $pre, $post) AS hl_metadata, " +
            "       snippet(s, 3, $pre, $post, '…', 16) AS snip_metadata, " +
            "       bm25(s, 4.0, 2.0, 0.5, 1.0) AS score, " +
            "       f.modified_utc " +
            "FROM file_search s " +
            "JOIN file_search_map m ON s.rowid = m.rowid " +
            "JOIN files f ON f.id = m.file_id " +
            "WHERE file_search MATCH $query " +
            "ORDER BY score " +
            "LIMIT $limit;";
        command.Parameters.Add("$query", SqliteType.Text).Value = query;
        command.Parameters.Add("$limit", SqliteType.Integer).Value = take;
        command.Parameters.Add("$pre", SqliteType.Text).Value = "[";
        command.Parameters.Add("$post", SqliteType.Text).Value = "]";

        var hits = new List<SearchHit>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var idBytes = (byte[])reader[0];
            var id = new Guid(idBytes);
            var title = reader.GetString(1);
            var mime = reader.GetString(2);
            var highlightValue = reader.IsDBNull(3) ? null : reader.GetString(3);
            var highlightedTitle = string.IsNullOrWhiteSpace(highlightValue) ? title : highlightValue;
            var snippet = SelectSnippet(reader);
            var rawScore = reader.IsDBNull(11) ? double.PositiveInfinity : reader.GetDouble(11);
            var modified = reader.GetString(12);
            var modifiedUtc = DateTimeOffset.Parse(modified, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            var score = NormalizeScore(rawScore);
            hits.Add(new SearchHit(id, highlightedTitle, mime, snippet, score, modifiedUtc));
        }

        return hits;
    }

    private static string? SelectSnippet(SqliteDataReader reader)
    {
        static string? ReadValue(SqliteDataReader source, int ordinal)
            => source.IsDBNull(ordinal) ? null : source.GetString(ordinal);

        var candidates = new[]
        {
            ReadValue(reader, 4) ?? ReadValue(reader, 3),
            ReadValue(reader, 10) ?? ReadValue(reader, 9),
            ReadValue(reader, 8) ?? ReadValue(reader, 7),
            ReadValue(reader, 6) ?? ReadValue(reader, 5),
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private SqliteConnection CreateConnection()
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("Infrastructure has not been initialised with a connection string.");
        }

        return new SqliteConnection(_options.ConnectionString);
    }

    private static double NormalizeScore(double rawScore)
    {
        if (double.IsNaN(rawScore) || double.IsInfinity(rawScore))
        {
            return 0d;
        }

        var clamped = Math.Max(0d, rawScore);
        return 1d / (1d + clamped);
    }
}
