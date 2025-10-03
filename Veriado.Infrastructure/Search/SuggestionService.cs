namespace Veriado.Infrastructure.Search;

using System.Text;

/// <summary>
/// Provides prefix-based autocomplete suggestions sourced from the SQLite suggestion index.
/// </summary>
internal sealed class SuggestionService : ISearchSuggestionService
{
    private readonly InfrastructureOptions _options;
    private readonly ILogger<SuggestionService> _logger;

    public SuggestionService(InfrastructureOptions options, ILogger<SuggestionService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            _logger.LogWarning("Suggestion lookup skipped because infrastructure is not initialised");
            return Array.Empty<SearchSuggestion>();
        }

        var match = NormalizePrefix(prefix) + "*";
        var lang = string.IsNullOrWhiteSpace(language) ? null : language.Trim().ToLowerInvariant();

        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            var builder = new StringBuilder();
            builder.Append("SELECT s.term, s.weight, s.lang, s.source_field FROM suggestions_fts f ");
            builder.Append("JOIN suggestions s ON s.id = f.rowid ");
            builder.Append("WHERE suggestions_fts MATCH $match ");
            if (!string.IsNullOrWhiteSpace(lang))
            {
                builder.Append("AND s.lang = $lang ");
            }

            builder.Append("ORDER BY s.weight DESC, s.term ASC LIMIT $limit;");
            command.CommandText = builder.ToString();
            command.Parameters.Add("$match", SqliteType.Text).Value = match;
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
}
