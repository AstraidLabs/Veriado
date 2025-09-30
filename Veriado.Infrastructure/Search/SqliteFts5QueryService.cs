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
            "SELECT m.file_id, bm25(s, 4.0, 0.1, 2.0, 0.8, 0.2) AS score " +
            "FROM file_search s " +
            "JOIN file_search_map m ON s.rowid = m.rowid " +
            "JOIN files f ON f.id = m.file_id " +
            "WHERE file_search MATCH $query " +
            "ORDER BY score, f.modified_utc DESC, CASE WHEN lower(s.title) = lower($raw) THEN 0 ELSE 1 END, s.title COLLATE NOCASE " +
            "LIMIT $take OFFSET $skip;";
        command.Parameters.Add("$query", SqliteType.Text).Value = matchQuery;
        command.Parameters.Add("$take", SqliteType.Integer).Value = take;
        command.Parameters.Add("$skip", SqliteType.Integer).Value = skip;
        command.Parameters.Add("$raw", SqliteType.Text).Value = matchQuery;

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
            "       COALESCE(s.title, '') AS title, " +
            "       COALESCE(s.mime, 'application/octet-stream') AS mime, " +
            "       COALESCE(s.author, '') AS author, " +
            "       highlight(s, 0, $pre, $post) AS hl_title, " +
            "       snippet(s, 0, $pre, $post, '…', 16) AS snip_title, " +
            "       highlight(s, 2, $pre, $post) AS hl_author, " +
            "       snippet(s, 2, $pre, $post, '…', 8) AS snip_author, " +
            "       highlight(s, 3, $pre, $post) AS hl_metadata_text, " +
            "       snippet(s, 3, $pre, $post, '…', 16) AS snip_metadata_text, " +
            "       highlight(s, 1, $pre, $post) AS hl_mime, " +
            "       snippet(s, 1, $pre, $post, '…', 8) AS snip_mime, " +
            "       highlight(s, 4, $pre, $post) AS hl_metadata_json, " +
            "       snippet(s, 4, $pre, $post, '…', 16) AS snip_metadata_json, " +
            "       COALESCE(s.metadata_text, '') AS metadata_text_value, " +
            "       COALESCE(s.metadata, '') AS metadata_json_value, " +
            "       bm25(s, 4.0, 0.1, 2.0, 0.8, 0.2) AS score, " +
            "       f.modified_utc " +
            "FROM file_search s " +
            "JOIN file_search_map m ON s.rowid = m.rowid " +
            "JOIN files f ON f.id = m.file_id " +
            "WHERE file_search MATCH $query " +
            "ORDER BY score, f.modified_utc DESC, CASE WHEN lower(s.title) = lower($raw) THEN 0 ELSE 1 END, s.title COLLATE NOCASE " +
            "LIMIT $limit;";
        command.Parameters.Add("$query", SqliteType.Text).Value = query;
        command.Parameters.Add("$limit", SqliteType.Integer).Value = take;
        command.Parameters.Add("$pre", SqliteType.Text).Value = "[";
        command.Parameters.Add("$post", SqliteType.Text).Value = "]";
        command.Parameters.Add("$raw", SqliteType.Text).Value = query;

        var hits = new List<SearchHit>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var idBytes = (byte[])reader[0];
            var id = new Guid(idBytes);
            var title = reader.GetString(1);
            var mime = reader.GetString(2);
            var highlightedTitle = ReadValue(reader, 4);
            highlightedTitle = string.IsNullOrWhiteSpace(highlightedTitle) ? title : highlightedTitle;

            var metadataTextValue = ReadValue(reader, 14);
            var metadataJsonValue = ReadValue(reader, 15);
            var rawSnippet = SelectSnippet(reader, out var snippetSource);
            var snippet = snippetSource switch
            {
                SnippetSource.MetadataJson => MetadataSnippetFormatter.Build(rawSnippet, metadataJsonValue)
                    ?? metadataTextValue
                    ?? rawSnippet,
                SnippetSource.MetadataText when string.IsNullOrWhiteSpace(rawSnippet)
                    => metadataTextValue,
                _ => rawSnippet,
            };

            var rawScore = reader.IsDBNull(16) ? double.PositiveInfinity : reader.GetDouble(16);
            var modified = reader.GetString(17);
            var modifiedUtc = DateTimeOffset.Parse(modified, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            var score = NormalizeScore(rawScore);
            hits.Add(new SearchHit(id, highlightedTitle, mime, snippet, score, modifiedUtc));
        }

        return hits;
    }

    private static string? SelectSnippet(SqliteDataReader reader, out SnippetSource source)
    {
        var candidates = new[]
        {
            new SnippetCandidate(SnippetSource.Title, snippetOrdinal: 5, highlightOrdinal: 4, fallbackOrdinal: 1),
            new SnippetCandidate(SnippetSource.Author, snippetOrdinal: 7, highlightOrdinal: 6, fallbackOrdinal: 3),
            new SnippetCandidate(SnippetSource.MetadataText, snippetOrdinal: 9, highlightOrdinal: 8, fallbackOrdinal: 14),
            new SnippetCandidate(SnippetSource.Mime, snippetOrdinal: 11, highlightOrdinal: 10, fallbackOrdinal: 2),
            new SnippetCandidate(SnippetSource.MetadataJson, snippetOrdinal: 13, highlightOrdinal: 12, fallbackOrdinal: 15),
        };

        foreach (var candidate in candidates)
        {
            var snippet = ReadValue(reader, candidate.SnippetOrdinal);
            if (string.IsNullOrWhiteSpace(snippet))
            {
                snippet = ReadValue(reader, candidate.HighlightOrdinal);
            }

            if (string.IsNullOrWhiteSpace(snippet) && candidate.FallbackOrdinal >= 0)
            {
                snippet = ReadValue(reader, candidate.FallbackOrdinal);
            }

            if (!string.IsNullOrWhiteSpace(snippet))
            {
                source = candidate.Source;
                return snippet;
            }
        }

        source = SnippetSource.None;
        return null;
    }

    private static string? ReadValue(SqliteDataReader source, int ordinal)
    {
        if (ordinal < 0 || source.IsDBNull(ordinal))
        {
            return null;
        }

        var value = source.GetString(ordinal);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private readonly struct SnippetCandidate
    {
        public SnippetCandidate(SnippetSource source, int snippetOrdinal, int highlightOrdinal, int fallbackOrdinal)
        {
            Source = source;
            SnippetOrdinal = snippetOrdinal;
            HighlightOrdinal = highlightOrdinal;
            FallbackOrdinal = fallbackOrdinal;
        }

        public SnippetSource Source { get; }

        public int SnippetOrdinal { get; }

        public int HighlightOrdinal { get; }

        public int FallbackOrdinal { get; }
    }

    private enum SnippetSource
    {
        None,
        Title,
        Author,
        MetadataText,
        Mime,
        MetadataJson,
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
