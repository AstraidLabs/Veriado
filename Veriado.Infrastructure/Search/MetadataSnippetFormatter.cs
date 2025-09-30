using System;
using System.Collections.Generic;
using Veriado.Domain.Search;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides helpers that translate raw metadata JSON snippets into human-readable summaries.
/// </summary>
internal static class MetadataSnippetFormatter
{
    public static string? Build(string? rawSnippet, string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        var summary = MetadataTextFormatter.BuildSummary(metadataJson);
        if (string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        var tokens = ExtractHighlightedTokens(rawSnippet);
        return tokens.Count == 0 ? summary : ApplyHighlights(summary!, tokens);
    }

    private static IReadOnlyList<string> ExtractHighlightedTokens(string? snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet))
        {
            return Array.Empty<string>();
        }

        var buffer = new List<string>();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var span = snippet.AsSpan();
        var index = 0;

        while (index < span.Length)
        {
            var openOffset = span[index..].IndexOf('[');
            if (openOffset < 0)
            {
                break;
            }

            var openIndex = index + openOffset;
            var closeOffset = span[(openIndex + 1)..].IndexOf(']');
            if (closeOffset < 0)
            {
                break;
            }

            var closeIndex = openIndex + 1 + closeOffset;
            if (closeIndex <= openIndex)
            {
                break;
            }

            var token = span[(openIndex + 1)..closeIndex].ToString().Trim('"', '\'', ':', ',', ' ', '\u2026');
            if (token.Length > 1 && unique.Add(token))
            {
                buffer.Add(token);
                if (buffer.Count >= 4)
                {
                    break;
                }
            }

            index = closeIndex + 1;
        }

        return buffer;
    }

    private static string ApplyHighlights(string summary, IReadOnlyList<string> tokens)
    {
        var result = summary;
        foreach (var token in tokens)
        {
            result = HighlightToken(result, token);
        }

        return result;
    }

    private static string HighlightToken(string text, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return text;
        }

        var comparison = StringComparison.OrdinalIgnoreCase;
        var searchIndex = 0;
        while (searchIndex < text.Length)
        {
            var index = text.IndexOf(token, searchIndex, comparison);
            if (index < 0)
            {
                break;
            }

            if (index > 0 && index + token.Length < text.Length && text[index - 1] == '[' && text[index + token.Length] == ']')
            {
                searchIndex = index + token.Length;
                continue;
            }

            return string.Concat(
                text.AsSpan(0, index),
                '[',
                text.AsSpan(index, token.Length),
                ']',
                text.AsSpan(index + token.Length));
        }

        return text;
    }
}
