using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Veriado.Appl.Search;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides a configurable analyzer implementation backed by <see cref="AnalyzerProfile"/> settings.
/// </summary>
public sealed class GeneralTextAnalyzer : ITextAnalyzer
{
    private static readonly Regex FilenameSplitRegex = new("[-_. ]", RegexOptions.Compiled);
    private static readonly Dictionary<char, string> SpecialFoldMap = new()
    {
        ['ß'] = "ss",
        ['ẞ'] = "ss",
        ['ø'] = "o",
        ['Ø'] = "o",
        ['đ'] = "d",
        ['Đ'] = "d",
        ['ð'] = "d",
        ['Ð'] = "d",
        ['þ'] = "th",
        ['Þ'] = "th",
    };

    private readonly AnalyzerProfile _profile;
    private readonly HashSet<string> _stopwords;
    private readonly IStemmer? _stemmer;

    /// <summary>
    /// Initialises a new instance of the <see cref="GeneralTextAnalyzer"/> class.
    /// </summary>
    public GeneralTextAnalyzer(AnalyzerProfile profile)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _stopwords = new HashSet<string>(profile.Stopwords?.Select(NormalizeToken) ?? Array.Empty<string>(), StringComparer.Ordinal);
        _stemmer = StemmerRegistry.Resolve(profile.Name, profile.EnableStemming);
    }

    /// <inheritdoc />
    public string Normalize(string text, string? profileOrLang = null)
    {
        if (!string.IsNullOrWhiteSpace(profileOrLang) && !string.Equals(profileOrLang, _profile.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Analyzer was created for profile '{_profile.Name}' and cannot process '{profileOrLang}'.");
        }

        return NormalizeInternal(text);
    }

    /// <inheritdoc />
    public IEnumerable<string> Tokenize(string text, string? profileOrLang = null)
    {
        if (!string.IsNullOrWhiteSpace(profileOrLang) && !string.Equals(profileOrLang, _profile.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Analyzer was created for profile '{_profile.Name}' and cannot process '{profileOrLang}'.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var normalized = NormalizeInternal(text);
        var segments = _profile.SplitFilenames ? SplitFilenameSegments(normalized) : new[] { normalized };

        foreach (var segment in segments)
        {
            if (segment.Length == 0)
            {
                continue;
            }

            foreach (var token in EnumerateTokens(segment))
            {
                if (token.Length == 0)
                {
                    continue;
                }

                if (_stopwords.Contains(token))
                {
                    continue;
                }

                var candidate = _stemmer is null ? token : _stemmer.Stem(token);
                if (!string.IsNullOrEmpty(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }

    private string NormalizeInternal(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var lower = text.ToLowerInvariant();
        return FoldDiacritics(lower);
    }

    private static IEnumerable<string> SplitFilenameSegments(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        var lastIndex = 0;
        foreach (Match match in FilenameSplitRegex.Matches(text))
        {
            var segmentLength = match.Index - lastIndex;
            if (segmentLength > 0)
            {
                yield return text.Substring(lastIndex, segmentLength);
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            yield return text[lastIndex..];
        }
    }

    private IEnumerable<string> EnumerateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsLetter(rune))
            {
                builder.Append(rune);
                continue;
            }

            if (Rune.IsDigit(rune))
            {
                if (_profile.KeepNumbers)
                {
                    builder.Append(rune);
                }
                else if (builder.Length > 0)
                {
                    yield return builder.ToString();
                    builder.Clear();
                }

                continue;
            }

            if (builder.Length > 0)
            {
                yield return builder.ToString();
                builder.Clear();
            }
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString();
        }
    }

    private static string FoldDiacritics(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            if (SpecialFoldMap.TryGetValue(ch, out var replacement))
            {
                builder.Append(replacement);
            }
            else
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string NormalizeToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        var lower = token.ToLowerInvariant();
        return FoldDiacritics(lower);
    }

    private interface IStemmer
    {
        string Stem(string token);
    }

    private static class StemmerRegistry
    {
        public static IStemmer? Resolve(string profileName, bool enabled)
        {
            if (!enabled)
            {
                return null;
            }

            if (string.Equals(profileName, "en", StringComparison.OrdinalIgnoreCase))
            {
                return new EnglishPorterStemmer();
            }

            return null;
        }
    }

    private sealed class EnglishPorterStemmer : IStemmer
    {
        public string Stem(string token)
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length <= 2)
            {
                return token;
            }

            var word = token;
            if (word.EndsWith("sses", StringComparison.Ordinal))
            {
                word = word[..^2];
            }
            else if (word.EndsWith("ies", StringComparison.Ordinal))
            {
                word = word[..^3] + "y";
            }
            else if (word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal))
            {
                word = word[..^1];
            }

            if (word.EndsWith("ing", StringComparison.Ordinal) && word.Length > 4)
            {
                var stem = word[..^3];
                if (ContainsVowel(stem))
                {
                    word = stem;
                }
            }
            else if (word.EndsWith("ed", StringComparison.Ordinal) && word.Length > 3)
            {
                var stem = word[..^2];
                if (ContainsVowel(stem))
                {
                    word = stem;
                }
            }

            if (word.EndsWith("ly", StringComparison.Ordinal) && word.Length > 4)
            {
                word = word[..^2];
            }
            else if (word.EndsWith("er", StringComparison.Ordinal) && word.Length > 4)
            {
                word = word[..^2];
            }

            return word;
        }

        private static bool ContainsVowel(string text)
        {
            foreach (var ch in text)
            {
                if (ch is 'a' or 'e' or 'i' or 'o' or 'u' or 'y')
                {
                    return true;
                }
            }

            return false;
        }
    }
}
