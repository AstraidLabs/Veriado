using System;
using System.Collections.Generic;
using System.Linq;

namespace Veriado.Appl.Search;

/// <summary>
/// Provides helpers to construct safe FTS5 match expressions from raw user input.
/// </summary>
public static class FtsQueryBuilder
{
    /// <summary>
    /// Builds an FTS5 match expression from the provided text.
    /// </summary>
    /// <param name="text">The raw search text.</param>
    /// <param name="prefix">Whether each term should use prefix search.</param>
    /// <param name="allTerms">Whether all terms must match (AND) or any term (OR).</param>
    /// <param name="factory">The analyzer factory used for tokenisation.</param>
    /// <param name="profile">The optional analyzer profile.</param>
    /// <returns>The constructed match expression or an empty string when no valid tokens were found.</returns>
    public static string BuildMatch(string text, bool prefix, bool allTerms, IAnalyzerFactory factory, string? profile = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var tokens = TextNormalization.Tokenize(text, factory, profile)
            .Where(static token => !string.IsNullOrWhiteSpace(token))
            .ToArray();

        if (tokens.Length == 0)
        {
            return string.Empty;
        }

        var combinator = allTerms ? " AND " : " OR ";
        var matchTokens = new List<string>(tokens.Length);
        foreach (var token in tokens)
        {
            if (IsReservedWord(token))
            {
                if (OriginatesFromRawInput(text, token))
                {
                    continue;
                }

                var escapedReserved = TextNormalization.EscapeMatchToken(token);
                if (!string.IsNullOrWhiteSpace(escapedReserved))
                {
                    matchTokens.Add($"\"{escapedReserved}\"");
                }

                continue;
            }

            var matchToken = prefix
                ? TextNormalization.BuildMatchPrefix(token)
                : TextNormalization.EscapeMatchToken(token);

            if (!string.IsNullOrWhiteSpace(matchToken))
            {
                matchTokens.Add(matchToken);
            }
        }

        return matchTokens.Count == 0 ? string.Empty : string.Join(combinator, matchTokens);
    }

    private static bool OriginatesFromRawInput(string rawText, string token)
    {
        if (string.IsNullOrEmpty(rawText))
        {
            return true;
        }

        var searchIndex = 0;
        while (true)
        {
            searchIndex = rawText.IndexOf(token, searchIndex, StringComparison.OrdinalIgnoreCase);
            if (searchIndex < 0)
            {
                return false;
            }

            var preceding = searchIndex == 0 || !IsWordCharacter(rawText[searchIndex - 1]);
            var followingIndex = searchIndex + token.Length;
            var following = followingIndex >= rawText.Length || !IsWordCharacter(rawText[followingIndex]);

            if (preceding && following)
            {
                return true;
            }

            searchIndex += token.Length;
        }
    }

    private static bool IsWordCharacter(char character)
        => char.IsLetterOrDigit(character) || character == '_';

    private static bool IsReservedWord(string token)
        => s_reservedWords.Contains(token);

    private static readonly HashSet<string> s_reservedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "and",
        "or",
        "not",
        "near",
    };

    /// <summary>
    /// Attempts to build a match expression from the provided text.
    /// </summary>
    /// <param name="text">The raw search text.</param>
    /// <param name="prefix">Whether each term should use prefix search.</param>
    /// <param name="allTerms">Whether all terms must match.</param>
    /// <param name="factory">The analyzer factory used for tokenisation.</param>
    /// <param name="profile">The optional analyzer profile.</param>
    /// <param name="match">When successful, contains the match expression.</param>
    /// <returns><see langword="true"/> when a valid expression was produced; otherwise <see langword="false"/>.</returns>
    public static bool TryBuild(string? text, bool prefix, bool allTerms, IAnalyzerFactory factory, out string match, string? profile = null)
    {
        match = BuildMatch(text ?? string.Empty, prefix, allTerms, factory, profile);
        return !string.IsNullOrWhiteSpace(match);
    }
}
