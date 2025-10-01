using System.Security.Cryptography;
using System.Text;
using Veriado.Appl.Search;

namespace Veriado.Infrastructure.Search;

internal sealed class SearchIndexSignatureCalculator : ISearchIndexSignatureCalculator
{
    private readonly IAnalyzerFactory _analyzerFactory;
    private readonly TrigramIndexOptions _trigramOptions;

    public SearchIndexSignatureCalculator(IAnalyzerFactory analyzerFactory, TrigramIndexOptions trigramOptions)
    {
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
        _trigramOptions = trigramOptions ?? throw new ArgumentNullException(nameof(trigramOptions));
    }

    public SearchIndexSignature Compute(FileEntity file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var analyzer = _analyzerFactory.Create();
        var tokens = new List<string>();
        var maxTokens = Math.Max(1, _trigramOptions.MaxTokens);
        var totalTokens = 0;
        var fields = _trigramOptions.Fields ?? Array.Empty<string>();
        var document = file.ToSearchDocument();

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

            foreach (var token in analyzer.Tokenize(candidate))
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                tokens.Add(token);
                totalTokens++;
                if (totalTokens >= maxTokens)
                {
                    goto LimitReached;
                }
            }
        }

    LimitReached:
        var tokenHash = tokens.Count == 0 ? null : ComputeHash(tokens);
        var normalizedTitle = string.IsNullOrWhiteSpace(file.Title) ? file.Name.Value : file.Title!;
        return new SearchIndexSignature(SearchIndexState.DefaultAnalyzerVersion, tokenHash, normalizedTitle);
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
}

public readonly record struct SearchIndexSignature(
    string AnalyzerVersion,
    string? TokenHash,
    string NormalizedTitle);
