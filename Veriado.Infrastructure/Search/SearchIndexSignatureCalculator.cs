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
    private readonly SearchOptions _searchOptions;

    public SearchIndexSignatureCalculator(IOptions<SearchOptions> searchOptions)
    {
        ArgumentNullException.ThrowIfNull(searchOptions);
        _searchOptions = searchOptions.Value ?? throw new ArgumentNullException(nameof(searchOptions));
    }

    public SearchIndexSignature Compute(FileEntity file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var normalizedTitle = string.IsNullOrWhiteSpace(file.Title) ? file.Name.Value : file.Title!;
        return new SearchIndexSignature(GetAnalyzerVersion(), tokenHash: null, normalizedTitle);
    }

    public string GetAnalyzerVersion()
    {
        var analyzerOptions = _searchOptions.Analyzer ?? new AnalyzerOptions();

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

        var snapshot = new
        {
            Analyzer = new
            {
                analyzerOptions.DefaultProfile,
                Profiles = profileSnapshots
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

}

public readonly record struct SearchIndexSignature(
    string AnalyzerVersion,
    string? TokenHash,
    string NormalizedTitle);
