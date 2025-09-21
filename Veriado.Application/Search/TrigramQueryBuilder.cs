using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Veriado.Application.Search;

/// <summary>
/// Provides helpers for constructing trigram match expressions compatible with SQLite FTS5.
/// </summary>
public static class TrigramQueryBuilder
{
    /// <summary>
    /// Builds a unique set of trigrams from the supplied text.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <returns>The distinct trigram tokens.</returns>
    public static IEnumerable<string> BuildTrigrams(string text)
    {
        return CollectUniqueTrigrams(text);
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

            foreach (var token in CollectUniqueTrigrams(value!))
            {
                accumulator.Add(token);
            }
        }

        if (accumulator.Count == 0)
        {
            return string.Empty;
        }

        var ordered = accumulator.ToList();
        ordered.Sort(StringComparer.Ordinal);
        return string.Join(' ', ordered);
    }

    private static List<string> CollectUniqueTrigrams(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        var normalised = Normalise(text);
        if (normalised.Length == 0)
        {
            return new List<string>();
        }

        var tokens = normalised.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return new List<string>();
        }

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            if (token.Length <= 3)
            {
                set.Add(token);
                continue;
            }

            for (var index = 0; index <= token.Length - 3; index++)
            {
                set.Add(token.Substring(index, 3));
            }
        }

        if (set.Count == 0)
        {
            return new List<string>();
        }

        var ordered = set.ToList();
        ordered.Sort(StringComparer.Ordinal);
        return ordered;
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
