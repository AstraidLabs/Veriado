namespace Veriado.Appl.Search;

/// <summary>
/// Provides helpers for constructing trigram match expressions compatible with the Lucene fallback pipeline.
/// </summary>
public static class TrigramQueryBuilder
{
    /// <summary>
    /// Defines the maximum number of tokens that will be emitted when building a trigram index entry.
    /// Keeping the limit reasonably low prevents pathological documents from bloating the FTS index
    /// while still capturing enough context for fuzzy matching.
    /// </summary>
    private const int MaxIndexTokens = 2048;

    /// <summary>
    /// Builds a unique set of trigrams from the supplied text.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <returns>The distinct trigram tokens.</returns>
    public static IEnumerable<string> BuildTrigrams(string text)
    {
        return CollectUniqueTrigramTokens(text);
    }

    /// <summary>
    /// Builds a trigram match expression using the provided text.
    /// </summary>
    /// <param name="text">The raw user input.</param>
    /// <param name="requireAllTerms">Whether all tokens must match.</param>
    /// <returns>The constructed match clause or an empty string.</returns>
    public static string BuildTrigramMatch(string text, bool requireAllTerms)
    {
        var tokens = CollectUniqueTrigrams(text);
        if (tokens.Count == 0)
        {
            return string.Empty;
        }

        var separator = requireAllTerms ? " AND " : " OR ";
        return string.Join(separator, tokens);
    }

    /// <summary>
    /// Attempts to build a trigram match expression from the provided text.
    /// </summary>
    /// <param name="text">The raw input.</param>
    /// <param name="requireAllTerms">Whether all tokens must match.</param>
    /// <param name="match">When successful, contains the generated match expression.</param>
    /// <returns><see langword="true"/> when a match expression was produced; otherwise <see langword="false"/>.</returns>
    public static bool TryBuild(string? text, bool requireAllTerms, out string match)
    {
        match = BuildTrigramMatch(text ?? string.Empty, requireAllTerms);
        return !string.IsNullOrWhiteSpace(match);
    }

    /// <summary>
    /// Builds a normalised trigram index value from the provided fields.
    /// </summary>
    /// <param name="values">The candidate text fields.</param>
    /// <returns>The content stored within the trigram index.</returns>
    public static string BuildIndexEntry(params string?[] values)
    {
        if (values is null || values.Length == 0)
        {
            return string.Empty;
        }

        var accumulator = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            CollectUniqueTrigramTokens(value!, accumulator, MaxIndexTokens);
            if (accumulator.Count >= MaxIndexTokens)
            {
                break;
            }
        }

        if (accumulator.Count == 0)
        {
            return string.Empty;
        }

        var ordered = accumulator.ToList();
        ordered.Sort(StringComparer.Ordinal);
        if (ordered.Count > MaxIndexTokens)
        {
            ordered = ordered.Take(MaxIndexTokens).ToList();
        }

        return string.Join(' ', ordered);
    }

    private static List<string> CollectUniqueTrigrams(string text)
    {
        var tokens = CollectUniqueTrigramTokens(text);
        if (tokens.Count == 0)
        {
            return tokens;
        }

        var formatted = new List<string>(tokens.Count);
        foreach (var token in tokens)
        {
            var matchToken = FormatMatchToken(token);
            if (string.IsNullOrEmpty(matchToken))
            {
                continue;
            }

            formatted.Add(matchToken);
        }

        return formatted;
    }

    private static List<string> CollectUniqueTrigramTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        var set = new HashSet<string>(StringComparer.Ordinal);
        CollectUniqueTrigramTokens(text, set, int.MaxValue);
        if (set.Count == 0)
        {
            return new List<string>();
        }

        var ordered = set.ToList();
        ordered.Sort(StringComparer.Ordinal);
        return ordered;
    }

    private static string FormatMatchToken(string token)
    {
        var escaped = TextNormalization.EscapeMatchToken(token);
        if (string.IsNullOrEmpty(escaped))
        {
            return string.Empty;
        }

        return RequiresLiteralQuoting(token)
            ? $"\"{escaped}\""
            : escaped;
    }

    private static bool RequiresLiteralQuoting(string token)
    {
        if (token.Length == 0)
        {
            return true;
        }

        if (token.Contains(' '))
        {
            return true;
        }

        return string.Equals(token, "and", StringComparison.Ordinal)
            || string.Equals(token, "or", StringComparison.Ordinal)
            || string.Equals(token, "not", StringComparison.Ordinal);
    }

    private static void CollectUniqueTrigramTokens(string text, HashSet<string> sink, int limit)
    {
        if (string.IsNullOrWhiteSpace(text) || sink.Count >= limit)
        {
            return;
        }

        var normalised = Normalise(text);
        if (normalised.Length == 0)
        {
            return;
        }

        var tokens = normalised.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return;
        }

        foreach (var token in tokens)
        {
            if (sink.Count >= limit)
            {
                break;
            }

            if (token.Length <= 3)
            {
                sink.Add(token);
                continue;
            }

            for (var index = 0; index <= token.Length - 3; index++)
            {
                if (sink.Count >= limit)
                {
                    return;
                }

                sink.Add(token.Substring(index, 3));
            }
        }
    }

    private static string Normalise(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousWhitespace = false;

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
                previousWhitespace = false;
            }
            else if (!previousWhitespace)
            {
                builder.Append(' ');
                previousWhitespace = true;
            }
        }

        return builder.ToString().Trim();
    }
}
