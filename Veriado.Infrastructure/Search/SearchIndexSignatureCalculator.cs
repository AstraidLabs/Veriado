using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Veriado.Appl.Abstractions;
using Veriado.Appl.Search;

namespace Veriado.Infrastructure.Search;

internal sealed class SearchIndexSignatureCalculator : ISearchIndexSignatureCalculator
{
    private readonly SearchOptions _searchOptions;
    private readonly IAnalyzerFactory _analyzerFactory;

    public SearchIndexSignatureCalculator(IOptions<SearchOptions> searchOptions, IAnalyzerFactory analyzerFactory)
    {
        ArgumentNullException.ThrowIfNull(searchOptions);
        ArgumentNullException.ThrowIfNull(analyzerFactory);
        _searchOptions = searchOptions.Value ?? throw new ArgumentNullException(nameof(searchOptions));
        _analyzerFactory = analyzerFactory;
    }

    public SearchIndexSignature Compute(FileEntity file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var document = file.ToSearchDocument();
        var analyzer = _analyzerFactory.Create();
        var normalizedTitle = NormalizeTitle(document.Title, analyzer);
        var tokenHash = ComputeTokenHash(document, analyzer);

        return new SearchIndexSignature(GetAnalyzerVersion(), tokenHash, normalizedTitle);
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

    private static string NormalizeTitle(string title, ITextAnalyzer analyzer)
    {
        var normalized = analyzer.Normalize(title, profileOrLang: null);
        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized;
    }

    private static string? ComputeTokenHash(SearchDocument document, ITextAnalyzer analyzer)
    {
        var tokens = new List<string>();

        void Collect(string? source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return;
            }

            foreach (var token in analyzer.Tokenize(source, profileOrLang: null))
            {
                if (!string.IsNullOrWhiteSpace(token))
                {
                    tokens.Add(token);
                }
            }
        }

        Collect(document.Title);
        Collect(document.Author);
        Collect(document.Mime);
        Collect(document.MetadataText);

        if (tokens.Count == 0)
        {
            return null;
        }

        using var sha = SHA256.Create();
        var payload = string.Join('\n', tokens);
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }
}
