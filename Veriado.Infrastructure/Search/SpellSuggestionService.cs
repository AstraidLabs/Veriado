namespace Veriado.Infrastructure.Search;

using System;
using System.Collections.Generic;
using System.Linq;
using Veriado.Appl.Search;

/// <summary>
/// Provides approximate spell-correction suggestions using trigram similarity against harvested terms.
/// </summary>
internal sealed class SpellSuggestionService : ISpellSuggestionService
{
    private readonly InfrastructureOptions _options;
    private readonly ILogger<SpellSuggestionService> _logger;
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, IReadOnlyList<DictionaryEntry>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly IAnalyzerFactory _analyzerFactory;

    public SpellSuggestionService(InfrastructureOptions options, ILogger<SpellSuggestionService> logger, IAnalyzerFactory analyzerFactory)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
    }

    public async Task<IReadOnlyList<SpellSuggestion>> SuggestAsync(
        string token,
        string? language,
        int limit,
        double threshold,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || limit <= 0)
        {
            return Array.Empty<SpellSuggestion>();
        }

        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            return Array.Empty<SpellSuggestion>();
        }

        var lang = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim().ToLowerInvariant();
        IReadOnlyList<DictionaryEntry> dictionary;
        lock (_syncRoot)
        {
            if (!_cache.TryGetValue(lang, out dictionary!))
            {
                dictionary = Array.Empty<DictionaryEntry>();
            }
        }

        if (dictionary.Count == 0)
        {
            dictionary = await LoadDictionaryAsync(lang, cancellationToken).ConfigureAwait(false);
            lock (_syncRoot)
            {
                _cache[lang] = dictionary;
            }
        }

        if (dictionary.Count == 0)
        {
            return Array.Empty<SpellSuggestion>();
        }

        var normalizedToken = NormalizeForTrigram(token);
        if (string.IsNullOrWhiteSpace(normalizedToken))
        {
            return Array.Empty<SpellSuggestion>();
        }

        var queryTrigrams = TrigramQueryBuilder.BuildTrigrams(normalizedToken)
            .Select(static t => t)
            .ToHashSet(StringComparer.Ordinal);
        if (queryTrigrams.Count == 0)
        {
            return Array.Empty<SpellSuggestion>();
        }

        var candidates = new List<SpellSuggestion>(Math.Min(limit * 2, 32));
        foreach (var entry in dictionary)
        {
            var similarity = ComputeSimilarity(queryTrigrams, entry.Trigrams);
            if (similarity >= threshold)
            {
                candidates.Add(new SpellSuggestion(entry.Term, similarity));
            }
        }

        if (candidates.Count == 0)
        {
            return Array.Empty<SpellSuggestion>();
        }

        var ordered = candidates
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Term, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();

        return ordered;
    }

    private async Task<IReadOnlyList<DictionaryEntry>> LoadDictionaryAsync(
        string language,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT term, weight FROM suggestions WHERE lang = $lang ORDER BY weight DESC LIMIT 5000;";
            command.Parameters.Add("$lang", SqliteType.Text).Value = language;

            var entries = new List<DictionaryEntry>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var term = reader.GetString(0);
                var weight = reader.IsDBNull(1) ? 1d : reader.GetDouble(1);
                var normalizedTerm = NormalizeForTrigram(term);
                if (string.IsNullOrWhiteSpace(normalizedTerm))
                {
                    continue;
                }

                var trigrams = TrigramQueryBuilder.BuildTrigrams(normalizedTerm)
                    .Select(static t => t)
                    .ToHashSet(StringComparer.Ordinal);
                if (trigrams.Count == 0)
                {
                    continue;
                }

                entries.Add(new DictionaryEntry(term, weight, trigrams));
            }

            _logger.LogInformation(
                "Loaded {Count} dictionary terms for spell suggestions ({Language})",
                entries.Count,
                language);
            return entries;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
        {
            _logger.LogDebug(ex, "Spell suggestion dictionary unavailable; ensure migrations have been applied.");
            return Array.Empty<DictionaryEntry>();
        }
    }

    private string NormalizeForTrigram(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var tokens = TextNormalization
            .Tokenize(text, _analyzerFactory)
            .Where(static token => !string.IsNullOrWhiteSpace(token))
            .ToArray();

        return tokens.Length == 0 ? string.Empty : string.Join(' ', tokens);
    }

    private static double ComputeSimilarity(HashSet<string> query, HashSet<string> candidate)
    {
        if (candidate.Count == 0)
        {
            return 0d;
        }

        var intersection = 0;
        foreach (var trigram in query)
        {
            if (candidate.Contains(trigram))
            {
                intersection++;
            }
        }

        if (intersection == 0)
        {
            return 0d;
        }

        var union = query.Count + candidate.Count - intersection;
        if (union == 0)
        {
            return 0d;
        }

        return intersection / (double)union;
    }

    private sealed record DictionaryEntry(string Term, double Weight, HashSet<string> Trigrams);
}
