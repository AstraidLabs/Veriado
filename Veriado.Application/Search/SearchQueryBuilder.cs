namespace Veriado.Appl.Search;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Veriado.Appl.Search.Abstractions;

/// <summary>
/// Default implementation of <see cref="ISearchQueryBuilder"/> that targets SQLite FTS5.
/// <para>
/// Example usage:
/// <code>
/// var builder = new SearchQueryBuilder();
/// var expression = builder.Or(
///     builder.And(
///         builder.Term("title", "report"),
///         builder.Phrase("author", "Alice Smith")),
///     builder.Phrase(null, "quarterly earnings"));
/// builder.Range("modified", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), null);
/// var plan = builder.Build(expression);
/// // plan.MatchExpression => "(title:report AND author:\"alice smith\") OR \"quarterly earnings\""
/// // plan.WhereClauses => ["f.modified_utc >= $p0"]
/// </code>
/// </para>
/// </summary>
public sealed class SearchQueryBuilder : ISearchQueryBuilder
{
    private const char Wildcard = '*';

    private static readonly IReadOnlyDictionary<string, string?> FieldMap = new Dictionary<string, string?>(
        StringComparer.OrdinalIgnoreCase)
    {
        ["title"] = "title",
        ["author"] = "author",
        ["mime"] = "mime",
        ["metadata_text"] = "metadata_text",
        ["metadata"] = "metadata",
        ["content"] = null,
        ["any"] = null,
    };

    private static readonly IReadOnlyDictionary<string, RangeTarget> RangeMap = new Dictionary<string, RangeTarget>(
        StringComparer.OrdinalIgnoreCase)
    {
        ["modified"] = new("f.modified_utc", SqliteType.Text, ConvertDateTime),
        ["modified_utc"] = new("f.modified_utc", SqliteType.Text, ConvertDateTime),
        ["created"] = new("f.created_utc", SqliteType.Text, ConvertDateTime),
        ["created_utc"] = new("f.created_utc", SqliteType.Text, ConvertDateTime),
        ["size"] = new("f.size_bytes", SqliteType.Integer, static value => value),
        ["size_bytes"] = new("f.size_bytes", SqliteType.Integer, static value => value),
    };

    private readonly List<string> _whereClauses = new();
    private readonly List<SqliteParameterDefinition> _parameters = new();
    private readonly SearchScorePlan _scorePlan;
    private readonly ISynonymProvider _synonymProvider;
    private readonly string _language;
    private readonly ITrigramQueryBuilder _trigramBuilder;

    private int _parameterIndex;

    /// <summary>
    /// Initialises a new instance of the <see cref="SearchQueryBuilder"/> class using the default configuration.
    /// </summary>
    public SearchQueryBuilder()
        : this(null, null, null, null)
    {
    }

    /// <summary>
    /// Initialises a new instance of the <see cref="SearchQueryBuilder"/> class with a custom synonym provider.
    /// </summary>
    /// <param name="synonymProvider">The synonym provider responsible for expanding terms.</param>
    /// <param name="language">Optional language identifier used when querying synonyms.</param>
    public SearchQueryBuilder(ISynonymProvider? synonymProvider, string? language)
        : this(null, synonymProvider, language, null)
    {
    }

    /// <summary>
    /// Initialises a new instance of the <see cref="SearchQueryBuilder"/> class with scoring options and custom services.
    /// </summary>
    /// <param name="scoreOptions">Optional scoring options applied to the resulting <see cref="SearchScorePlan"/>.</param>
    /// <param name="synonymProvider">The synonym provider responsible for expanding terms.</param>
    /// <param name="language">Optional language identifier used when querying synonyms.</param>
    public SearchQueryBuilder(
        SearchScoreOptions? scoreOptions,
        ISynonymProvider? synonymProvider,
        string? language,
        ITrigramQueryBuilder? trigramBuilder = null)
    {
        _scorePlan = new SearchScorePlan();
        ApplyScoreOptions(scoreOptions);
        _synonymProvider = synonymProvider ?? EmptySynonymProvider.Instance;
        _language = string.IsNullOrWhiteSpace(language)
            ? "en"
            : language!.Trim().ToLowerInvariant();
        _trigramBuilder = trigramBuilder
            ?? new TrigramQueryBuilder(Options.Create(new SearchOptions()));
    }

    private void ApplyScoreOptions(SearchScoreOptions? options)
    {
        if (options is null)
        {
            return;
        }

        _scorePlan.TitleWeight = options.TitleWeight;
        _scorePlan.MimeWeight = options.MimeWeight;
        _scorePlan.AuthorWeight = options.AuthorWeight;
        _scorePlan.MetadataTextWeight = options.MetadataTextWeight;
        _scorePlan.MetadataWeight = options.MetadataWeight;
        _scorePlan.ScoreMultiplier = options.ScoreMultiplier;
        _scorePlan.HigherScoreIsBetter = options.HigherScoreIsBetter;
        _scorePlan.UseTfIdfAlternative = options.UseTfIdfAlternative;
        _scorePlan.TfIdfDampingFactor = options.TfIdfDampingFactor;
    }

    /// <inheritdoc />
    public QueryNode? Term(string? field, string term)
    {
        var token = ExtractSingleToken(term);
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return new TokenNode(NormalizeField(field), token, QueryTokenType.Term);
    }

    /// <inheritdoc />
    public QueryNode? Phrase(string? field, string phrase)
    {
        var normalized = NormalizeText(phrase);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return new TokenNode(NormalizeField(field), normalized, QueryTokenType.Phrase);
    }

    /// <inheritdoc />
    public QueryNode? Proximity(string? field, string first, string second, int distance)
    {
        if (distance <= 0)
        {
            distance = 1;
        }

        var firstToken = ExtractSingleToken(first);
        var secondToken = ExtractSingleToken(second);
        if (string.IsNullOrWhiteSpace(firstToken) || string.IsNullOrWhiteSpace(secondToken))
        {
            return null;
        }

        var builder = new StringBuilder(firstToken!.Length + secondToken!.Length + 16);
        builder.Append('"').Append(firstToken).Append('"');
        builder.Append(' ').Append("NEAR/").Append(distance).Append(' ');
        builder.Append('"').Append(secondToken).Append('"');

        return new TokenNode(NormalizeField(field), builder.ToString(), QueryTokenType.Proximity);
    }

    /// <inheritdoc />
    public QueryNode? Prefix(string? field, string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return null;
        }

        var trimmed = prefix.Trim();
        var hasWildcard = trimmed.EndsWith(Wildcard);
        var core = hasWildcard ? trimmed.TrimEnd(Wildcard) : trimmed;
        var token = ExtractSingleToken(core);
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var value = hasWildcard ? token + Wildcard : token + Wildcard;
        return new TokenNode(NormalizeField(field), value, QueryTokenType.Prefix);
    }

    /// <inheritdoc />
    public QueryNode? Fuzzy(string? field, string term, bool requireAllTerms = false)
    {
        if (Term(field, term) is not TokenNode token)
        {
            return null;
        }

        var expression = _trigramBuilder.BuildTrigramMatch(term ?? string.Empty, requireAllTerms);
        if (string.IsNullOrWhiteSpace(expression))
        {
            return token;
        }

        return token with { TrigramExpression = expression, RequiresAllTrigramTerms = requireAllTerms };
    }

    /// <inheritdoc />
    public QueryNode? And(params QueryNode?[] nodes)
        => Combine(BooleanOperator.And, nodes);

    /// <inheritdoc />
    public QueryNode? Or(params QueryNode?[] nodes)
        => Combine(BooleanOperator.Or, nodes);

    /// <inheritdoc />
    public QueryNode? Not(QueryNode? node)
    {
        if (node is null)
        {
            return null;
        }

        return new NotNode(node);
    }

    /// <inheritdoc />
    public void Range(string field, object? from, object? to, bool includeLower = true, bool includeUpper = true)
    {
        if (!RangeMap.TryGetValue(field, out var target))
        {
            return;
        }

        if (from is not null)
        {
            var parameter = CreateParameter(from, target, includeLower ? ">=" : ">");
            if (parameter is not null)
            {
                _whereClauses.Add(parameter.Value.Clause);
                _parameters.Add(parameter.Value.Parameter);
            }
        }

        if (to is not null)
        {
            var parameter = CreateParameter(to, target, includeUpper ? "<=" : "<");
            if (parameter is not null)
            {
                _whereClauses.Add(parameter.Value.Clause);
                _parameters.Add(parameter.Value.Parameter);
            }
        }
    }

    /// <inheritdoc />
    public void Boost(string field, double factor)
    {
        if (factor <= 0d)
        {
            return;
        }

        switch (NormalizeField(field))
        {
            case "title":
                _scorePlan.TitleWeight *= factor;
                break;
            case "author":
                _scorePlan.AuthorWeight *= factor;
                break;
            case "mime":
                _scorePlan.MimeWeight *= factor;
                break;
            case "metadata_text":
                _scorePlan.MetadataTextWeight *= factor;
                break;
            case "metadata":
                _scorePlan.MetadataWeight *= factor;
                break;
            default:
                break;
        }
    }

    /// <inheritdoc />
    public void UseTfIdfRanking(double dampingFactor = 0.5d)
    {
        if (double.IsNaN(dampingFactor) || double.IsInfinity(dampingFactor) || dampingFactor < 0d)
        {
            dampingFactor = 0.5d;
        }

        _scorePlan.UseTfIdfAlternative = true;
        _scorePlan.TfIdfDampingFactor = dampingFactor;
        _scorePlan.HigherScoreIsBetter = true;
    }

    /// <inheritdoc />
    public void UseRankExpression(string sqlExpression, bool higherIsBetter = false)
    {
        if (!string.IsNullOrWhiteSpace(sqlExpression))
        {
            _scorePlan.CustomRankExpression = sqlExpression;
            _scorePlan.HigherScoreIsBetter = higherIsBetter;
        }
    }

    /// <inheritdoc />
    public void UseCustomSimilaritySql(string sqlExpression)
    {
        if (!string.IsNullOrWhiteSpace(sqlExpression))
        {
            _scorePlan.CustomSimilaritySql = sqlExpression;
        }
    }

    /// <inheritdoc />
    public void UseCustomSimilarity(Func<double, double?, DateTimeOffset?, double> similarity)
    {
        ArgumentNullException.ThrowIfNull(similarity);
        _scorePlan.CustomSimilarityDelegate = similarity;
    }

    /// <inheritdoc />
    public SearchQueryPlan Build(QueryNode? root, string? rawQuery = null)
    {
        var match = BuildMatch(root);
        var artifacts = CollectArtifacts(root);
        var trigramExpression = artifacts.TrigramExpression;
        var requiresTrigramFallback = !string.IsNullOrWhiteSpace(trigramExpression);

        if (string.IsNullOrWhiteSpace(match) && !requiresTrigramFallback)
        {
            throw new InvalidOperationException("Search query must produce a non-empty MATCH expression.");
        }

        match = string.IsNullOrWhiteSpace(match) ? string.Empty : match!;
        trigramExpression = string.IsNullOrWhiteSpace(trigramExpression) ? null : trigramExpression;

        return new SearchQueryPlan(
            match,
            _whereClauses.AsReadOnly(),
            _parameters.AsReadOnly(),
            _scorePlan,
            requiresTrigramFallback,
            trigramExpression,
            rawQuery,
            artifacts.RequiresTrigramForWildcard,
            artifacts.HasPrefix,
            artifacts.HasExplicitFuzzy,
            artifacts.HasHeuristicFuzzy);
    }

    private QueryNode? Combine(BooleanOperator op, params QueryNode?[] nodes)
    {
        if (nodes is null || nodes.Length == 0)
        {
            return null;
        }

        var materialised = new List<QueryNode>();
        foreach (var node in nodes)
        {
            if (node is null)
            {
                continue;
            }

            if (node is BooleanNode booleanNode && booleanNode.Operator == op)
            {
                materialised.AddRange(booleanNode.Children);
            }
            else
            {
                materialised.Add(node);
            }
        }

        if (materialised.Count == 0)
        {
            return null;
        }

        if (materialised.Count == 1)
        {
            return materialised[0];
        }

        return new BooleanNode(op, materialised);
    }

    private string BuildMatch(QueryNode? node)
    {
        if (node is null)
        {
            return string.Empty;
        }

        return node switch
        {
            TokenNode token => FormatToken(token),
            BooleanNode boolean => FormatBoolean(boolean),
            NotNode notNode =>
                BuildMatch(notNode.Operand) is var operand && !string.IsNullOrWhiteSpace(operand)
                    ? $"NOT ({operand})"
                    : string.Empty,
            _ => string.Empty,
        };
    }

    private string FormatToken(TokenNode token)
    {
        var fieldPrefix = string.IsNullOrWhiteSpace(token.Field) ? string.Empty : token.Field + ':';
        return token.TokenType switch
        {
            QueryTokenType.Term => FormatTerm(fieldPrefix, token.Value),
            QueryTokenType.Phrase => fieldPrefix + '"' + EscapeQuotes(token.Value) + '"',
            QueryTokenType.Proximity => fieldPrefix + token.Value,
            QueryTokenType.Prefix => fieldPrefix + token.Value,
            _ => string.Empty,
        };
    }

    private string FormatBoolean(BooleanNode node)
    {
        if (node.Children.Count == 0)
        {
            return string.Empty;
        }

        var op = node.Operator == BooleanOperator.And ? " AND " : " OR ";
        var parts = node.Children
            .Select(BuildMatch)
            .Where(static part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        if (parts.Length == 0)
        {
            return string.Empty;
        }

        if (parts.Length == 1)
        {
            return parts[0];
        }

        return "(" + string.Join(op, parts) + ")";
    }

    private string FormatTerm(string fieldPrefix, string tokenValue)
    {
        var expansions = ExpandSynonyms(tokenValue);
        if (expansions.Count == 0)
        {
            return fieldPrefix + tokenValue;
        }

        if (expansions.Count == 1)
        {
            var single = expansions[0];
            return fieldPrefix + (single.Contains(' ', StringComparison.Ordinal)
                ? '"' + EscapeQuotes(single) + '"'
                : single);
        }

        var builder = new StringBuilder();
        builder.Append(fieldPrefix).Append('(');
        for (var index = 0; index < expansions.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(" OR ");
            }

            var expansion = expansions[index];
            if (expansion.Contains(' ', StringComparison.Ordinal))
            {
                builder.Append('"').Append(EscapeQuotes(expansion)).Append('"');
            }
            else
            {
                builder.Append(expansion);
            }
        }

        builder.Append(')');
        return builder.ToString();
    }

    private static string? NormalizeField(string? field)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            return null;
        }

        return FieldMap.TryGetValue(field.Trim(), out var value) ? value : field.Trim().ToLowerInvariant();
    }

    private static string? ExtractSingleToken(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = NormalizeText(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length == 0 ? null : tokens[0];
    }

    private IReadOnlyList<string> ExpandSynonyms(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return Array.Empty<string>();
        }

        var expanded = _synonymProvider.Expand(_language, term);
        if (expanded.Count == 0)
        {
            return Array.Empty<string>();
        }

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>(expanded.Count);
        foreach (var candidate in expanded)
        {
            var normalised = NormalizeText(candidate);
            if (string.IsNullOrWhiteSpace(normalised))
            {
                continue;
            }

            if (unique.Add(normalised))
            {
                ordered.Add(normalised);
            }
        }

        if (ordered.Count == 0)
        {
            var fallback = NormalizeText(term);
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                ordered.Add(fallback);
            }
        }

        return ordered.Count == 0 ? Array.Empty<string>() : ordered;
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var previousWhitespace = false;
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.')
            {
                builder.Append(char.ToLowerInvariant(ch));
                previousWhitespace = false;
            }
            else if (char.IsWhiteSpace(ch))
            {
                if (!previousWhitespace)
                {
                    builder.Append(' ');
                    previousWhitespace = true;
                }
            }
            else
            {
                if (!previousWhitespace)
                {
                    builder.Append(' ');
                    previousWhitespace = true;
                }
            }
        }

        return builder.ToString().Trim();
    }

    private static string EscapeQuotes(string value)
        => value.Replace("\"", "\"\"");

    private SearchArtifacts CollectArtifacts(QueryNode? node)
    {
        if (node is null)
        {
            return SearchArtifacts.Empty;
        }

        return node switch
        {
            TokenNode token => BuildTokenArtifacts(token),
            BooleanNode boolean => BuildBooleanArtifacts(boolean),
            NotNode notNode => BuildNotArtifacts(notNode),
            _ => SearchArtifacts.Empty,
        };
    }

    private SearchArtifacts BuildTokenArtifacts(TokenNode token)
    {
        var trigram = BuildTokenTrigram(token);
        var requiresWildcard = token.TokenType == QueryTokenType.Wildcard;
        var hasPrefix = token.TokenType == QueryTokenType.Prefix;
        var hasExplicitFuzzy = token.TokenType == QueryTokenType.Fuzzy && !token.IsHeuristicFuzzy;
        var hasHeuristicFuzzy = token.TokenType == QueryTokenType.Fuzzy && token.IsHeuristicFuzzy;

        return new SearchArtifacts(trigram, requiresWildcard, hasPrefix, hasExplicitFuzzy, hasHeuristicFuzzy);
    }

    private SearchArtifacts BuildBooleanArtifacts(BooleanNode node)
    {
        var parts = new List<string>();
        var requiresWildcard = false;
        var hasPrefix = false;
        var hasExplicitFuzzy = false;
        var hasHeuristicFuzzy = false;

        foreach (var child in node.Children)
        {
            var artifacts = CollectArtifacts(child);
            requiresWildcard |= artifacts.RequiresTrigramForWildcard;
            hasPrefix |= artifacts.HasPrefix;
            hasExplicitFuzzy |= artifacts.HasExplicitFuzzy;
            hasHeuristicFuzzy |= artifacts.HasHeuristicFuzzy;

            if (!string.IsNullOrWhiteSpace(artifacts.TrigramExpression))
            {
                parts.Add(WrapTrigram(artifacts.TrigramExpression!));
            }
        }

        var trigram = CombineTrigramParts(parts, node.Operator);
        return new SearchArtifacts(trigram, requiresWildcard, hasPrefix, hasExplicitFuzzy, hasHeuristicFuzzy);
    }

    private SearchArtifacts BuildNotArtifacts(NotNode node)
    {
        var artifacts = CollectArtifacts(node.Operand);
        if (string.IsNullOrWhiteSpace(artifacts.TrigramExpression))
        {
            return artifacts;
        }

        var trigram = "NOT " + WrapTrigram(artifacts.TrigramExpression!);
        return artifacts with { TrigramExpression = trigram };
    }

    private static string? CombineTrigramParts(IReadOnlyList<string> parts, BooleanOperator op)
    {
        if (parts.Count == 0)
        {
            return null;
        }

        if (parts.Count == 1)
        {
            return parts[0];
        }

        var separator = op == BooleanOperator.And ? " AND " : " OR ";
        return "(" + string.Join(separator, parts) + ")";
    }

    private string? BuildTokenTrigram(TokenNode token)
    {
        if (!string.IsNullOrWhiteSpace(token.TrigramExpression))
        {
            return token.TrigramExpression;
        }

        return token.TokenType switch
        {
            QueryTokenType.Term => BuildTextTrigram(token.Value, requireAllTerms: true),
            QueryTokenType.Phrase => BuildTextTrigram(token.Value, requireAllTerms: true),
            QueryTokenType.Prefix => BuildPrefixTrigram(token.Value),
            QueryTokenType.Wildcard => BuildWildcardTrigram(token.Value),
            QueryTokenType.Fuzzy => BuildTextTrigram(token.Value, token.RequiresAllTrigramTerms),
            _ => null,
        };
    }

    private string? BuildTextTrigram(string value, bool requireAllTerms)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var expression = _trigramBuilder.BuildTrigramMatch(value, requireAllTerms);
        return string.IsNullOrWhiteSpace(expression) ? null : expression;
    }

    private string? BuildPrefixTrigram(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var core = value.TrimEnd(Wildcard);
        if (string.IsNullOrWhiteSpace(core))
        {
            return null;
        }

        return BuildTextTrigram(core, requireAllTerms: true);
    }

    private string? BuildWildcardTrigram(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var segments = ExtractWildcardSegments(value);
        if (segments.Count == 0)
        {
            return null;
        }

        var expressions = new List<string>(segments.Count);
        foreach (var segment in segments)
        {
            var expression = BuildTextTrigram(segment, requireAllTerms: true);
            if (string.IsNullOrWhiteSpace(expression))
            {
                continue;
            }

            expressions.Add(WrapTrigram(expression));
        }

        if (expressions.Count == 0)
        {
            return null;
        }

        var distinct = expressions.Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count == 1)
        {
            return distinct[0];
        }

        return "(" + string.Join(" OR ", distinct) + ")";
    }

    private static List<string> ExtractWildcardSegments(string value)
    {
        var segments = new List<string>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return segments;
        }

        var current = new StringBuilder();
        foreach (var ch in value)
        {
            if (ch is '*' or '?')
            {
                AppendCurrent();
            }
            else
            {
                current.Append(ch);
            }
        }

        AppendCurrent();
        return segments;

        void AppendCurrent()
        {
            if (current.Length < 2)
            {
                current.Clear();
                return;
            }

            var segment = current.ToString().Trim();
            current.Clear();
            if (segment.Length >= 2)
            {
                segments.Add(segment);
            }
        }
    }

    private static string WrapTrigram(string expression)
    {
        var trimmed = expression.Trim();
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        if (trimmed.StartsWith('(') && trimmed.EndsWith(')'))
        {
            return trimmed;
        }

        if (trimmed.StartsWith("NOT ", StringComparison.OrdinalIgnoreCase))
        {
            return "(" + trimmed + ")";
        }

        if (trimmed.Contains(" AND ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains(" OR ", StringComparison.OrdinalIgnoreCase))
        {
            return "(" + trimmed + ")";
        }

        return trimmed;
    }

    private readonly record struct SearchArtifacts(
        string? TrigramExpression,
        bool RequiresTrigramForWildcard,
        bool HasPrefix,
        bool HasExplicitFuzzy,
        bool HasHeuristicFuzzy)
    {
        public static SearchArtifacts Empty { get; } = new(null, false, false, false, false);
    }

    private (string Clause, SqliteParameterDefinition Parameter)? CreateParameter(
        object value,
        RangeTarget target,
        string op)
    {
        var converted = target.Converter(value);
        if (converted is null)
        {
            return null;
        }

        var parameterName = NextParameterName();
        var parameter = new SqliteParameterDefinition(parameterName, converted, target.Type);
        var clause = $"{target.Column} {op} {parameterName}";
        return (clause, parameter);
    }

    private string NextParameterName()
        => "$p" + _parameterIndex++;

    private static object? ConvertDateTime(object value)
    {
        return value switch
        {
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            DateTime dt => DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
            string s when DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
                => dto.ToString("O", CultureInfo.InvariantCulture),
            _ => value,
        };
    }

    private readonly record struct RangeTarget(string Column, SqliteType Type, Func<object, object?> Converter);

    private sealed class EmptySynonymProvider : ISynonymProvider
    {
        public static readonly EmptySynonymProvider Instance = new();

        public IReadOnlyList<string> Expand(string language, string term)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return Array.Empty<string>();
            }

            return new[] { Normalize(term) };

            static string Normalize(string value)
            {
                var builder = new StringBuilder(value.Length);
                foreach (var ch in value)
                {
                    builder.Append(char.ToLowerInvariant(ch));
                }

                return builder.ToString();
            }
        }
    }
}
