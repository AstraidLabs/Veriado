namespace Veriado.Infrastructure.Search;

using System.Text;
using Veriado.Infrastructure.Persistence.Connections;

/// <summary>
/// Provides prefix-based autocomplete suggestions sourced from the SQLite suggestion index.
/// </summary>
internal sealed class SuggestionService : ISearchSuggestionService
{
    private readonly ILogger<SuggestionService> _logger;
    private readonly IConnectionStringProvider _connectionStringProvider;

    public SuggestionService(
        ILogger<SuggestionService> logger,
        IConnectionStringProvider connectionStringProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
    }

    public async Task<IReadOnlyList<SearchSuggestion>> SuggestAsync(
        string prefix,
        string? language,
        int limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prefix) || limit <= 0)
        {
            return Array.Empty<SearchSuggestion>();
        }

        var normalizedPrefix = NormalizePrefix(prefix);
        if (string.IsNullOrWhiteSpace(normalizedPrefix))
        {
            return Array.Empty<SearchSuggestion>();
        }

        var likePattern = BuildLikePattern(normalizedPrefix);
        var lang = string.IsNullOrWhiteSpace(language) ? null : language.Trim().ToLowerInvariant();

        try
        {
            await using var connection = _connectionStringProvider.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken: cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            var builder = new StringBuilder();
            builder.Append("SELECT s.term, s.weight, s.lang, s.source_field FROM suggestions s ");
            builder.Append("WHERE lower(s.term) LIKE $pattern ESCAPE '\\' ");
            if (!string.IsNullOrWhiteSpace(lang))
            {
                builder.Append("AND s.lang = $lang ");
            }

            builder.Append("ORDER BY s.weight DESC, s.term COLLATE NOCASE ASC LIMIT $limit;");
            command.CommandText = builder.ToString();
            command.Parameters.Add("$pattern", SqliteType.Text).Value = likePattern;
            if (!string.IsNullOrWhiteSpace(lang))
            {
                command.Parameters.Add("$lang", SqliteType.Text).Value = lang;
            }

            command.Parameters.Add("$limit", SqliteType.Integer).Value = limit;

            var results = new List<SearchSuggestion>(Math.Min(limit, 32));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var term = reader.GetString(0);
                var weight = reader.IsDBNull(1) ? 1d : reader.GetDouble(1);
                var langValue = reader.IsDBNull(2) ? "en" : reader.GetString(2);
                var source = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                results.Add(new SearchSuggestion(term, weight, langValue, source));
            }

            return results;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
        {
            _logger.LogDebug(ex, "Suggestion index unavailable; ensure migrations have been applied.");
            return Array.Empty<SearchSuggestion>();
        }
    }

    private static string NormalizePrefix(string prefix)
    {
        var builder = new StringBuilder(prefix.Length);
        foreach (var ch in prefix)
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-')
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (char.IsWhiteSpace(ch))
            {
                builder.Append(' ');
            }
        }

        return builder.ToString().Trim();
    }

    private static string BuildLikePattern(string prefix)
    {
        var builder = new StringBuilder(prefix.Length + 1);
        foreach (var ch in prefix)
        {
            if (ch is '_' or '%')
            {
                builder.Append('\\');
            }

            builder.Append(ch);
        }

        builder.Append('%');
        return builder.ToString();
    }
}
