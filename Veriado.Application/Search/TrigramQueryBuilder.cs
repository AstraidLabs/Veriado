using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Veriado.Appl.Search;

public interface ITrigramQueryBuilder
{
    IReadOnlyList<string> BuildTrigrams(string text, int? maxTokens = null);

    string BuildTrigramMatch(string text, bool requireAllTerms);

    bool TryBuild(string? text, bool requireAllTerms, out string match);

    string BuildIndexEntry(params string?[] values);
}

/// <summary>
/// Provides helpers for constructing trigram match expressions compatible with SQLite FTS5.
/// </summary>
public sealed class TrigramQueryBuilder : ITrigramQueryBuilder
{
    internal const int DefaultMaxIndexTokens = 2048;

    private readonly SearchOptions _options;

    [ActivatorUtilitiesConstructor]
    public TrigramQueryBuilder(IOptions<SearchOptions> options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Builds a unique set of trigram tokens from the supplied text.
    /// </summary>
    public IReadOnlyList<string> BuildTrigrams(string text, int? maxTokens = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var limit = ResolveLimit(maxTokens);
        var sink = new HashSet<string>(StringComparer.Ordinal);
        CollectUniqueTrigramTokens(text, sink, limit);
        if (sink.Count == 0)
        {
            return Array.Empty<string>();
        }

        var ordered = sink.ToList();
        ordered.Sort(StringComparer.Ordinal);
        if (ordered.Count > limit)
        {
            ordered = ordered.Take(limit).ToList();
        }

        return ordered;
    }

    /// <summary>
    /// Builds a trigram match expression using the provided text.
    /// </summary>
    public string BuildTrigramMatch(string text, bool requireAllTerms)
    {
        var limit = ResolveLimit(null);
        var tokens = CollectUniqueTrigrams(text, limit);
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
    public bool TryBuild(string? text, bool requireAllTerms, out string match)
    {
        match = BuildTrigramMatch(text ?? string.Empty, requireAllTerms);
        return !string.IsNullOrWhiteSpace(match);
    }

    /// <summary>
    /// Builds a normalised trigram index value from the provided fields.
    /// </summary>
    public string BuildIndexEntry(params string?[] values)
    {
        if (values is null || values.Length == 0)
        {
            return string.Empty;
        }

        var limit = ResolveLimit(null);
        var accumulator = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            CollectUniqueTrigramTokens(value!, accumulator, limit);
            if (accumulator.Count >= limit)
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
        if (ordered.Count > limit)
        {
            ordered = ordered.Take(limit).ToList();
        }

        return string.Join(' ', ordered);
    }

    private List<string> CollectUniqueTrigrams(string text, int limit)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        var tokens = BuildTrigrams(text, limit);
        if (tokens.Count == 0)
        {
            return new List<string>();
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

    private void CollectUniqueTrigramTokens(string text, HashSet<string> sink, int limit)
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

    private int ResolveLimit(int? overrideLimit)
    {
        var limit = overrideLimit ?? _options.Trigram?.MaxTokens ?? DefaultMaxIndexTokens;
        if (limit <= 0)
        {
            return DefaultMaxIndexTokens;
        }

        return limit;
    }
}
