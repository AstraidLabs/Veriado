using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Veriado.Domain.Search;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides Lucene.Net backed query capabilities complementing the SQLite FTS index.
/// </summary>
internal sealed class LuceneSearchQueryService
{
    private const float ScoreTolerance = 0.0001f;

    private readonly InfrastructureOptions _options;
    private readonly Analyzer? _analyzer;
    private readonly FSDirectory? _directory;
    private static readonly IReadOnlyDictionary<string, float> FieldBoosts = new Dictionary<string, float>
    {
        [LuceneSearchFields.Title] = 4f,
        [LuceneSearchFields.Author] = 2f,
    };

    private static readonly string[] QueryFields =
    {
        LuceneSearchFields.Title,
        LuceneSearchFields.Author,
    };

    public LuceneSearchQueryService(InfrastructureOptions options)
    {
        _options = options;
        if (!options.EnableLuceneIntegration)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.LuceneIndexPath))
        {
            return;
        }

        var directoryInfo = new DirectoryInfo(options.LuceneIndexPath);
        if (!directoryInfo.Exists)
        {
            directoryInfo.Create();
        }

        _analyzer = new LuceneMetadataAnalyzer();
        _directory = FSDirectory.Open(directoryInfo);
    }

    public bool IsEnabled => _options.EnableLuceneIntegration && _directory is not null && _analyzer is not null;

    public Task<IReadOnlyList<(Guid Id, double Score)>> SearchWithScoresAsync(
        string matchQuery,
        int skip,
        int take,
        CancellationToken cancellationToken)
        => SearchWithScoresInternalAsync(matchQuery, skip, take, allowFuzzy: false, cancellationToken);

    public Task<IReadOnlyList<(Guid Id, double Score)>> SearchFuzzyWithScoresAsync(
        string matchQuery,
        int skip,
        int take,
        CancellationToken cancellationToken)
        => SearchWithScoresInternalAsync(matchQuery, skip, take, allowFuzzy: true, cancellationToken);

    public Task<int> CountAsync(string matchQuery, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matchQuery);
        if (!IsEnabled)
        {
            return Task.FromResult(0);
        }

        var query = TryParse(matchQuery, allowFuzzy: false);
        if (query is null)
        {
            return Task.FromResult(0);
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var reader = TryOpenReader();
        if (reader is null)
        {
            return Task.FromResult(0);
        }

        var searcher = new IndexSearcher(reader);
        var collector = new TotalHitCountCollector();
        searcher.Search(query, collector);
        return Task.FromResult(collector.TotalHits);
    }

    public Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int? limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var take = limit.GetValueOrDefault(10);
        if (take <= 0)
        {
            return Task.FromResult<IReadOnlyList<SearchHit>>(Array.Empty<SearchHit>());
        }

        if (!IsEnabled)
        {
            return Task.FromResult<IReadOnlyList<SearchHit>>(Array.Empty<SearchHit>());
        }

        cancellationToken.ThrowIfCancellationRequested();

        var parsedQuery = TryParse(query, allowFuzzy: false) ?? TryParse(QueryParserBase.Escape(query), allowFuzzy: false);
        if (parsedQuery is null)
        {
            return Task.FromResult<IReadOnlyList<SearchHit>>(Array.Empty<SearchHit>());
        }

        using var reader = TryOpenReader();
        if (reader is null)
        {
            return Task.FromResult<IReadOnlyList<SearchHit>>(Array.Empty<SearchHit>());
        }

        var searcher = new IndexSearcher(reader);
        var topDocs = searcher.Search(parsedQuery, take);
        if (topDocs.ScoreDocs.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<SearchHit>>(Array.Empty<SearchHit>());
        }

        var maxScore = Math.Max(topDocs.MaxScore, ScoreTolerance);
        var hits = new List<SearchHit>(Math.Min(take, topDocs.ScoreDocs.Length));
        foreach (var scoreDoc in topDocs.ScoreDocs)
        {
            var document = searcher.Doc(scoreDoc.Doc);
            if (!TryExtractIdentifier(document, out var fileId))
            {
                continue;
            }

            var title = document.Get(LuceneSearchFields.Title) ?? string.Empty;
            var mime = document.Get(LuceneSearchFields.Mime) ?? "application/octet-stream";
            var author = document.Get(LuceneSearchFields.Author);
            var modified = TryParseDate(document.Get(LuceneSearchFields.Modified));
            var score = NormalizeScore(scoreDoc.Score, maxScore);
            hits.Add(new SearchHit(fileId, title, mime, author, score, modified));
        }

        return Task.FromResult<IReadOnlyList<SearchHit>>(hits);
    }

    private Task<IReadOnlyList<(Guid Id, double Score)>> SearchWithScoresInternalAsync(
        string matchQuery,
        int skip,
        int take,
        bool allowFuzzy,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matchQuery);
        if (take <= 0)
        {
            return Task.FromResult<IReadOnlyList<(Guid, double)>>(Array.Empty<(Guid, double)>());
        }

        if (skip < 0)
        {
            skip = 0;
        }

        if (!IsEnabled)
        {
            return Task.FromResult<IReadOnlyList<(Guid, double)>>(Array.Empty<(Guid, double)>());
        }

        cancellationToken.ThrowIfCancellationRequested();

        var query = TryParse(matchQuery, allowFuzzy) ?? TryParse(QueryParserBase.Escape(matchQuery), allowFuzzy);
        if (query is null)
        {
            return Task.FromResult<IReadOnlyList<(Guid, double)>>(Array.Empty<(Guid, double)>());
        }

        using var reader = TryOpenReader();
        if (reader is null)
        {
            return Task.FromResult<IReadOnlyList<(Guid, double)>>(Array.Empty<(Guid, double)>());
        }

        var maxDocs = Math.Max(skip + take, take);
        var searcher = new IndexSearcher(reader);
        var topDocs = searcher.Search(query, maxDocs);
        if (topDocs.ScoreDocs.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<(Guid, double)>>(Array.Empty<(Guid, double)>());
        }

        var maxScore = Math.Max(topDocs.MaxScore, ScoreTolerance);
        var upperBound = Math.Min(skip + take, topDocs.ScoreDocs.Length);
        if (skip >= upperBound)
        {
            return Task.FromResult<IReadOnlyList<(Guid, double)>>(Array.Empty<(Guid, double)>());
        }

        var results = new List<(Guid, double)>(Math.Max(upperBound - skip, 0));
        for (var index = skip; index < upperBound; index++)
        {
            var scoreDoc = topDocs.ScoreDocs[index];
            var document = searcher.Doc(scoreDoc.Doc);
            if (!TryExtractIdentifier(document, out var fileId))
            {
                continue;
            }

            var score = NormalizeScore(scoreDoc.Score, maxScore);
            results.Add((fileId, score));
        }

        return Task.FromResult<IReadOnlyList<(Guid, double)>>(results);
    }

    private DirectoryReader? TryOpenReader()
    {
        if (_directory is null)
        {
            return null;
        }

        try
        {
            if (!DirectoryReader.IndexExists(_directory))
            {
                return null;
            }

            return DirectoryReader.Open(_directory);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private Query? TryParse(string text, bool allowFuzzy)
    {
        if (string.IsNullOrWhiteSpace(text) || _analyzer is null)
        {
            return null;
        }

        var parser = new MultiFieldQueryParser(LuceneVersion.LUCENE_48, QueryFields, _analyzer, FieldBoosts)
        {
            DefaultOperator = QueryParser.Operator.AND,
            AllowLeadingWildcard = false,
        };

        if (allowFuzzy)
        {
            parser.FuzzyMinSim = 0.6f;
            parser.FuzzyPrefixLength = 1;
        }

        try
        {
            var parsed = parser.Parse(text);
            return CombineWithMimeQueries(parsed, text);
        }
        catch (ParseException)
        {
            return null;
        }
    }

    private static bool TryExtractIdentifier(Document document, out Guid fileId)
    {
        fileId = default;
        var identifier = document.Get(LuceneSearchFields.Id);
        return identifier is not null && Guid.TryParseExact(identifier, "N", out fileId);
    }

    private static Query? CombineWithMimeQueries(Query? parsed, string text)
    {
        var builder = new BooleanQuery.Builder();
        if (parsed is not null)
        {
            builder.Add(parsed, Occur.SHOULD);
        }

        foreach (var mimeQuery in BuildMimeQueries(text))
        {
            builder.Add(new BoostQuery(mimeQuery, 1f), Occur.SHOULD);
        }

        var combined = builder.Build();
        return combined.Clauses.Count == 0 ? null : combined;
    }

    private static IEnumerable<Query> BuildMimeQueries(string text)
    {
        var prefixes = new HashSet<string>(StringComparer.Ordinal);
        var wildcards = new HashSet<string>(StringComparer.Ordinal);

        foreach (var token in ExtractMimeTokens(text))
        {
            if (token.Contains('/', StringComparison.Ordinal))
            {
                if (prefixes.Add(token))
                {
                    yield return new PrefixQuery(new Term(LuceneSearchFields.Mime, token));
                    if (!token.EndsWith('/', StringComparison.Ordinal))
                    {
                        yield return new TermQuery(new Term(LuceneSearchFields.Mime, token));
                    }
                }
            }
            else if (token.Length >= 3 && wildcards.Add(token))
            {
                yield return new WildcardQuery(new Term(LuceneSearchFields.Mime, $"*{token}*"));
            }
        }
    }

    private static IEnumerable<string> ExtractMimeTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var normalised = NormaliseMime(text);
        var parts = normalised.Split(MimeSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in parts)
        {
            if (seen.Add(part))
            {
                yield return part;
            }
        }

        foreach (var candidate in text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!candidate.Contains('/', StringComparison.Ordinal))
            {
                continue;
            }

            var normalisedCandidate = NormaliseMime(candidate);
            if (normalisedCandidate.Length == 0)
            {
                continue;
            }

            if (seen.Add(normalisedCandidate))
            {
                yield return normalisedCandidate;
            }
        }
    }

    private static string NormaliseMime(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

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
            }
            else if (Array.IndexOf(MimeSeparators, ch) >= 0)
            {
                builder.Append(ch);
            }
            else if (char.IsWhiteSpace(ch))
            {
                builder.Append(' ');
            }
        }

        return builder.ToString();
    }

    private static readonly char[] MimeSeparators = { '/', '-', '_', '.', ' ' };

    private static DateTimeOffset TryParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DateTimeOffset.MinValue;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
    }

    private static double NormalizeScore(float score, float maxScore)
    {
        if (maxScore <= ScoreTolerance || score <= ScoreTolerance)
        {
            return 0d;
        }

        var normalised = score / maxScore;
        return Math.Clamp(normalised, 0d, 1d);
    }
}
