using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Veriado.Appl.Search;

/// <summary>
/// Provides helpers to construct safe FTS5 match expressions from raw user input.
/// </summary>
internal static class FtsQueryBuilder
{
    /// <summary>
    /// Escapes a single term ensuring that dangerous characters are removed.
    /// </summary>
    /// <param name="term">The raw term.</param>
    /// <returns>The escaped term.</returns>
    public static string EscapeTerm(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(term.Length);
        foreach (var ch in term)
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.')
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (char.IsWhiteSpace(ch))
            {
                builder.Append(' ');
            }
            else
            {
                builder.Append(' ');
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Builds an FTS5 match expression from the provided text.
    /// </summary>
    /// <param name="text">The raw search text.</param>
    /// <param name="prefix">Whether each term should use prefix search.</param>
    /// <param name="allTerms">Whether all terms must match (AND) or any term (OR).</param>
    /// <returns>The constructed match expression or an empty string when no valid tokens were found.</returns>
    public static string BuildMatch(string text, bool prefix, bool allTerms)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var tokens = Tokenize(text)
            .Select(EscapeTerm)
            .Where(static token => !string.IsNullOrWhiteSpace(token))
            .ToArray();

        if (tokens.Length == 0)
        {
            return string.Empty;
        }

        var suffix = prefix ? "*" : string.Empty;
        var combinator = allTerms ? " AND " : " OR ";
        return string.Join(combinator, tokens.Select(token => token + suffix));
    }

    /// <summary>
    /// Attempts to build a match expression from the provided text.
    /// </summary>
    /// <param name="text">The raw search text.</param>
    /// <param name="prefix">Whether each term should use prefix search.</param>
    /// <param name="allTerms">Whether all terms must match.</param>
    /// <param name="match">When successful, contains the match expression.</param>
    /// <returns><see langword="true"/> when a valid expression was produced; otherwise <see langword="false"/>.</returns>
    public static bool TryBuild(string? text, bool prefix, bool allTerms, out string match)
    {
        match = BuildMatch(text ?? string.Empty, prefix, allTerms);
        return !string.IsNullOrWhiteSpace(match);
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var current = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }
}
