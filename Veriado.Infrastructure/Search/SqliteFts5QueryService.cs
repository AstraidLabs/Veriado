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
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT m.file_id, 1.0 / (1.0 + bm25(s)) AS score " +
            "FROM file_search s " +
            "JOIN file_search_map m ON s.rowid = m.rowid " +
            "WHERE file_search MATCH $query " +
            "ORDER BY score DESC " +
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
            var score = reader.IsDBNull(1) ? 0d : reader.GetDouble(1);
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
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT m.file_id, " +
            "       COALESCE(s.title, ''), " +
            "       COALESCE(s.mime, 'application/octet-stream'), " +
            "       snippet(s, 3, '[', ']', '…', 10) AS snippet, " +
            "       1.0 / (1.0 + bm25(s)) AS score, " +
            "       f.modified_utc " +
            "FROM file_search s " +
            "JOIN file_search_map m ON s.rowid = m.rowid " +
            "JOIN files f ON f.id = m.file_id " +
            "WHERE file_search MATCH $query " +
            "ORDER BY score DESC " +
            "LIMIT $limit;";
        command.Parameters.Add("$query", SqliteType.Text).Value = query;
        command.Parameters.Add("$limit", SqliteType.Integer).Value = take;

        var hits = new List<SearchHit>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var idBytes = (byte[])reader[0];
            var id = new Guid(idBytes);
            var title = reader.GetString(1);
            var mime = reader.GetString(2);
            var snippet = reader.IsDBNull(3) ? null : reader.GetString(3);
            var score = reader.IsDBNull(4) ? 0d : reader.GetDouble(4);
            var modified = reader.GetString(5);
            var modifiedUtc = DateTimeOffset.Parse(modified, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            hits.Add(new SearchHit(id, title, mime, snippet, score, modifiedUtc));
        }

        return hits;
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
