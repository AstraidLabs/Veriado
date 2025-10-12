using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Veriado.Appl.Search;

namespace Veriado.Infrastructure.Search;

internal sealed class SearchIndexSignatureCalculator : ISearchIndexSignatureCalculator
{
    private readonly TrigramIndexOptions _trigramOptions;
    private readonly ITrigramQueryBuilder _trigramBuilder;
    private readonly SearchOptions _searchOptions;

    public SearchIndexSignatureCalculator(
        TrigramIndexOptions trigramOptions,
        ITrigramQueryBuilder trigramBuilder,
        IOptions<SearchOptions> searchOptions)
    {
        _trigramOptions = trigramOptions ?? throw new ArgumentNullException(nameof(trigramOptions));
        _trigramBuilder = trigramBuilder ?? throw new ArgumentNullException(nameof(trigramBuilder));
        ArgumentNullException.ThrowIfNull(searchOptions);
        _searchOptions = searchOptions.Value ?? throw new ArgumentNullException(nameof(searchOptions));
    }

    public SearchIndexSignature Compute(FileEntity file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var document = file.ToSearchDocument();
        var trigramEntry = BuildTrigramEntry(document);
        var tokens = string.IsNullOrWhiteSpace(trigramEntry)
            ? Array.Empty<string>()
            : trigramEntry.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tokenHash = tokens.Length == 0 ? null : ComputeHash(tokens);
        var normalizedTitle = string.IsNullOrWhiteSpace(file.Title) ? file.Name.Value : file.Title!;
        return new SearchIndexSignature(GetAnalyzerVersion(), tokenHash, normalizedTitle);
    }

    public string GetAnalyzerVersion()
    {
        var analyzerOptions = _searchOptions.Analyzer ?? new AnalyzerOptions();
        var trigramOptions = _searchOptions.Trigram ?? new TrigramIndexOptions();

        var analyzerProfiles = analyzerOptions.Profiles
            ?? new Dictionary<string, AnalyzerProfile>(StringComparer.OrdinalIgnoreCase);

        var profileSnapshots = analyzerProfiles
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp =>
            {
                var profile = kvp.Value ?? new AnalyzerProfile();
                var stopwords = (profile.Stopwords ?? Array.Empty<string>())
                    .OrderBy(word => word, StringComparer.Ordinal)
                    .ToArray();
                var customFilters = (profile.CustomFilters ?? Array.Empty<string>())
                    .OrderBy(filter => filter, StringComparer.Ordinal)
                    .ToArray();

                return new
                {
                    Key = kvp.Key,
                    Profile = new
                    {
                        profile.Name,
                        profile.EnableStemming,
                        profile.KeepNumbers,
                        profile.SplitFilenames,
                        profile.CustomTokenizer,
                        Stopwords = stopwords,
                        CustomFilters = customFilters,
                    }
                };
            })
            .ToArray();

        var trigramFields = (trigramOptions.Fields ?? Array.Empty<string>())
            .OrderBy(field => field, StringComparer.Ordinal)
            .ToArray();

        var snapshot = new
        {
            Analyzer = new
            {
                analyzerOptions.DefaultProfile,
                Profiles = profileSnapshots
            },
            Trigram = new
            {
                trigramOptions.MaxTokens,
                Fields = trigramFields
            }
        };

        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    private static string? ResolveFieldValue(string field, SearchDocument document)
    {
        return field.Trim().ToLowerInvariant() switch
        {
            "title" => document.Title,
            "author" => document.Author,
            "filename" => document.FileName,
            "metadata_text" => document.MetadataText,
            _ => null,
        };
    }

    private static string ComputeHash(IReadOnlyList<string> tokens)
    {
        var joined = string.Join("\n", tokens);
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private string BuildTrigramEntry(SearchDocument document)
    {
        var fields = _trigramOptions.Fields ?? Array.Empty<string>();
        if (fields.Length == 0)
        {
            return string.Empty;
        }

        var values = new List<string>(fields.Length);
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                continue;
            }

            var candidate = ResolveFieldValue(field, document);
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            values.Add(candidate!);
        }

        if (values.Count == 0)
        {
            return string.Empty;
        }

        return _trigramBuilder.BuildIndexEntry(values.ToArray());
    }
}

public readonly record struct SearchIndexSignature(
    string AnalyzerVersion,
    string? TokenHash,
    string NormalizedTitle);
