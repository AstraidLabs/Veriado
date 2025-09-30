using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Veriado.Appl.Search;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Executes trigram-based fuzzy queries using the existing SQLite FTS5 index.
/// </summary>
internal sealed class TrigramQueryService
{
    private const char Ellipsis = '…';

    private readonly InfrastructureOptions _options;

    public TrigramQueryService(InfrastructureOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
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

        var queryTokens = ExtractTokens(matchQuery);
        if (queryTokens.Count == 0)
        {
            return Array.Empty<(Guid, double)>();
        }

        var fetch = skip + take;
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT m.file_id, t.trgm " +
            "FROM file_trgm t " +
            "JOIN file_trgm_map m ON t.rowid = m.rowid " +
            "WHERE file_trgm MATCH $query " +
            "ORDER BY bm25(t) ASC, m.rowid ASC " +
            "LIMIT $limit;";
        command.Parameters.Add("$query", SqliteType.Text).Value = matchQuery;
        command.Parameters.Add("$limit", SqliteType.Integer).Value = fetch;

        var results = new List<(Guid Id, double Score)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var idBytes = (byte[])reader[0];
            var id = new Guid(idBytes);
            var trigrams = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var candidateTokens = ExtractTokens(trigrams);
            if (candidateTokens.Count == 0)
            {
                continue;
            }

            var score = ComputeJaccard(queryTokens, candidateTokens);
            if (score <= 0d)
            {
                continue;
            }

            results.Add((id, score));
        }

        if (results.Count == 0)
        {
            return results;
        }

        var ordered = results
            .OrderByDescending(static item => item.Score)
            .ThenBy(static item => item.Id)
            .Skip(skip)
            .Take(take)
            .ToList();

        return ordered;
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int take, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (take <= 0)
        {
            return Array.Empty<SearchHit>();
        }

        if (!_options.IsFulltextAvailable)
        {
            return Array.Empty<SearchHit>();
        }

        if (!TrigramQueryBuilder.TryBuild(query, requireAllTerms: false, out var matchQuery))
        {
            return Array.Empty<SearchHit>();
        }

        var queryTokens = TrigramQueryBuilder.BuildTrigrams(query)
            .Select(static token => token.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        if (queryTokens.Count == 0)
        {
            return Array.Empty<SearchHit>();
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT tm.file_id, " +
            "       sm.rowid, " +
            "       COALESCE(s.title, '') AS title, " +
            "       COALESCE(s.mime, '') AS mime, " +
            "       COALESCE(s.author, '') AS author, " +
            "       COALESCE(s.metadata_text, '') AS metadata_text, " +
            "       COALESCE(s.metadata, '') AS metadata_json, " +
            "       t.trgm, " +
            "       f.modified_utc " +
            "FROM file_trgm t " +
            "JOIN file_trgm_map tm ON t.rowid = tm.rowid " +
            "JOIN file_search_map sm ON sm.file_id = tm.file_id " +
            "JOIN file_search s ON s.rowid = sm.rowid " +
            "JOIN files f ON f.id = tm.file_id " +
            "WHERE file_trgm MATCH $query " +
            "ORDER BY bm25(t) ASC, tm.rowid ASC " +
            "LIMIT $limit;";
        command.Parameters.Add("$query", SqliteType.Text).Value = matchQuery;
        command.Parameters.Add("$limit", SqliteType.Integer).Value = take;

        var hits = new List<SearchHit>(take);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var idBytes = (byte[])reader[0];
            var fileId = new Guid(idBytes);
            var title = reader.GetString(2);
            var mime = reader.GetString(3);
            var author = reader.GetString(4);
            var metadataText = reader.GetString(5);
            var metadataJson = reader.GetString(6);
            var candidate = reader.IsDBNull(7) ? string.Empty : reader.GetString(7);
            var modifiedUtcRaw = reader.GetString(8);
            var modifiedUtc = DateTimeOffset.Parse(modifiedUtcRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

            var candidateTokens = ExtractTokens(candidate);
            if (candidateTokens.Count == 0)
            {
                continue;
            }

            var score = ComputeJaccard(queryTokens, candidateTokens);
            if (score <= 0d)
            {
                continue;
            }

            var snippetSource = !string.IsNullOrWhiteSpace(metadataText) ? "metadata_text" : "title";
            var sourceText = snippetSource == "metadata_text" ? metadataText : title;
            var snippet = BuildSnippet(sourceText, queryTokens);
            var highlights = BuildHighlights(snippetSource, snippet, queryTokens);
            var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = string.IsNullOrWhiteSpace(title) ? null : title,
                ["mime"] = string.IsNullOrWhiteSpace(mime) ? null : mime,
                ["author"] = string.IsNullOrWhiteSpace(author) ? null : author,
                ["metadata_text"] = string.IsNullOrWhiteSpace(metadataText) ? null : metadataText,
                ["metadata"] = string.IsNullOrWhiteSpace(metadataJson) ? null : metadataJson,
            };
            fields["last_modified_utc"] = modifiedUtc.ToString("O", CultureInfo.InvariantCulture);

            var sortValues = new SearchHitSortValues(modifiedUtc, score, score, score);
            hits.Add(new SearchHit(fileId, score, "TRIGRAM", snippetSource, snippet, highlights, fields, sortValues));
        }

        if (hits.Count == 0)
        {
            return hits;
        }

        return hits
            .OrderByDescending(static hit => hit.Score)
            .ThenBy(static hit => hit.Id)
            .ToList();
    }

    private static double ComputeJaccard(IReadOnlyCollection<string> query, IReadOnlyCollection<string> candidate)
    {
        if (query.Count == 0 || candidate.Count == 0)
        {
            return 0d;
        }

        var intersection = query.Intersect(candidate, StringComparer.Ordinal).Count();
        if (intersection == 0)
        {
            return 0d;
        }

        var union = query.Count + candidate.Count - intersection;
        if (union <= 0)
        {
            return 0d;
        }

        return Math.Clamp((double)intersection / union, 0d, 1d);
    }

    private static HashSet<string> ExtractTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var tokens = text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static token => !string.Equals(token, "AND", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(token, "OR", StringComparison.OrdinalIgnoreCase))
            .Select(static token => token.ToLowerInvariant());

        return tokens.ToHashSet(StringComparer.Ordinal);
    }

    private static string BuildSnippet(string text, IReadOnlyCollection<string> tokens)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.Trim();
        var normalized = NormalizeForComparison(trimmed, out var map);
        if (tokens.Count == 0 || normalized.Length == 0)
        {
            return trimmed.Length <= 240 ? trimmed : trimmed[..240] + Ellipsis;
        }

        var bestIndex = int.MaxValue;
        foreach (var token in tokens)
        {
            if (token.Length == 0)
            {
                continue;
            }

            var index = normalized.IndexOf(token, StringComparison.Ordinal);
            if (index >= 0 && index < bestIndex)
            {
                bestIndex = index;
            }
        }

        if (bestIndex == int.MaxValue || bestIndex >= map.Count)
        {
            return trimmed.Length <= 240 ? trimmed : trimmed[..240] + Ellipsis;
        }

        var matchStart = map[bestIndex];
        var start = Math.Max(0, matchStart - 60);
        var end = Math.Min(trimmed.Length, start + 240);

        var snippet = trimmed.Substring(start, end - start);
        if (start > 0)
        {
            snippet = Ellipsis + snippet;
        }

        if (end < trimmed.Length)
        {
            snippet += Ellipsis;
        }

        return snippet;
    }

    private static IReadOnlyList<HighlightSpan> BuildHighlights(string field, string snippet, IReadOnlyCollection<string> tokens)
    {
        if (string.IsNullOrEmpty(snippet) || tokens.Count == 0)
        {
            return Array.Empty<HighlightSpan>();
        }

        var normalized = NormalizeForComparison(snippet, out var map);
        if (normalized.Length == 0 || map.Count == 0)
        {
            return Array.Empty<HighlightSpan>();
        }

        var spans = new List<HighlightSpan>();
        var seen = new HashSet<(int Start, int Length)>();

        foreach (var token in tokens)
        {
            if (token.Length == 0)
            {
                continue;
            }

            var searchIndex = 0;
            while (searchIndex < normalized.Length)
            {
                var index = normalized.IndexOf(token, searchIndex, StringComparison.Ordinal);
                if (index < 0)
                {
                    break;
                }

                var lastIndex = Math.Min(index + token.Length - 1, map.Count - 1);
                var start = map[index];
                var end = map[lastIndex];
                var length = end - start + 1;
                if (length > 0)
                {
                    var key = (start, length);
                    if (seen.Add(key))
                    {
                        var term = SafeSubstring(snippet, start, length);
                        spans.Add(new HighlightSpan(field, start, length, term));
                    }
                }

                searchIndex = index + token.Length;
            }
        }

        return spans;
    }

    private static string SafeSubstring(string text, int start, int length)
    {
        if (start < 0)
        {
            start = 0;
        }

        if (start >= text.Length)
        {
            return string.Empty;
        }

        if (length <= 0)
        {
            return string.Empty;
        }

        if (start + length > text.Length)
        {
            length = text.Length - start;
        }

        return text.Substring(start, length);
    }

    private static string NormalizeForComparison(string text, out List<int> indexMap)
    {
        var builder = new StringBuilder(text.Length);
        indexMap = new List<int>(text.Length);
        var position = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            var runeLength = rune.Utf16SequenceLength;
            var decomposed = rune.ToString().Normalize(NormalizationForm.FormD);
            foreach (var ch in decomposed)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                {
                    continue;
                }

                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(char.ToLowerInvariant(ch));
                    indexMap.Add(position);
                }
                else
                {
                    builder.Append(' ');
                    indexMap.Add(position);
                }
            }

            position += runeLength;
        }

        return builder.ToString();
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
