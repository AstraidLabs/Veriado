using System;
using System.Linq;
using System.Text;

namespace Veriado.Appl.Search;

/// <summary>
/// Provides helper methods for working with analyzer driven text normalisation.
/// </summary>
public static class TextNormalization
{
    /// <summary>
    /// Normalises the supplied text using the analyzer created by the provided factory.
    /// </summary>
    public static string NormalizeText(string s, IAnalyzerFactory factory, string? profile = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var text = s ?? string.Empty;
        var analyzer = factory.Create(profile);
        return analyzer.Normalize(text, profile);
    }

    /// <summary>
    /// Tokenises the supplied text using the analyzer created by the provided factory.
    /// </summary>
    public static IEnumerable<string> Tokenize(string s, IAnalyzerFactory factory, string? profile = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var text = s ?? string.Empty;
        var analyzer = factory.Create(profile);
        return analyzer.Tokenize(text, profile);
    }

    /// <summary>
    /// Builds a quoted MATCH phrase from the supplied text.
    /// </summary>
    public static string BuildMatchPhrase(string s, IAnalyzerFactory factory, string? profile = null)
    {
        var tokens = Tokenize(s, factory, profile).Where(static token => !string.IsNullOrWhiteSpace(token)).ToArray();
        if (tokens.Length == 0)
        {
            return string.Empty;
        }

        var phrase = string.Join(' ', tokens);
        var escaped = EscapeMatchToken(phrase);
        if (string.IsNullOrEmpty(escaped))
        {
            return string.Empty;
        }

        return $"\"{escaped}\"";
    }

    /// <summary>
    /// Builds a prefix token suitable for SQLite FTS5 queries.
    /// </summary>
    public static string BuildMatchPrefix(string token)
    {
        var escaped = EscapeMatchToken(token);
        return string.IsNullOrEmpty(escaped) ? string.Empty : escaped + '*';
    }

    /// <summary>
    /// Escapes characters that would otherwise break an FTS5 MATCH token.
    /// </summary>
    public static string EscapeMatchToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(token.Length);
        foreach (var ch in token)
        {
            if (ch == '\"')
            {
                builder.Append("\"\"");
            }
            else if (ch == '\'')
            {
                builder.Append("''");
            }
            else
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }
}
