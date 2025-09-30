namespace Veriado.Appl.Search;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;
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
    private readonly SearchScorePlan _scorePlan = new();
    private readonly List<(string Expression, bool RequireAll)> _trigramExpressions = new();
    private readonly ISynonymProvider _synonymProvider;
    private readonly string _language;

    private bool _requiresTrigramFallback;
    private int _parameterIndex;

    /// <summary>
    /// Initialises a new instance of the <see cref="SearchQueryBuilder"/> class using the default configuration.
    /// </summary>
    public SearchQueryBuilder()
        : this(null, null)
    {
    }

    /// <summary>
    /// Initialises a new instance of the <see cref="SearchQueryBuilder"/> class with a custom synonym provider.
    /// </summary>
    /// <param name="synonymProvider">The synonym provider responsible for expanding terms.</param>
    /// <param name="language">Optional language identifier used when querying synonyms.</param>
    public SearchQueryBuilder(ISynonymProvider? synonymProvider, string? language)
    {
        _synonymProvider = synonymProvider ?? EmptySynonymProvider.Instance;
        _language = string.IsNullOrWhiteSpace(language)
            ? "en"
            : language!.Trim().ToLowerInvariant();
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
        var expression = TrigramQueryBuilder.BuildTrigramMatch(term ?? string.Empty, requireAllTerms);
        if (!string.IsNullOrWhiteSpace(expression))
        {
            _requiresTrigramFallback = true;
            _trigramExpressions.Add((expression, requireAllTerms));
        }

        return Term(field, term);
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
        if (string.IsNullOrWhiteSpace(match))
        {
            throw new InvalidOperationException("Search query must produce a non-empty MATCH expression.");
        }

        var trigramExpression = BuildTrigramExpression();
        return new SearchQueryPlan(
            match,
            _whereClauses.AsReadOnly(),
            _parameters.AsReadOnly(),
            _scorePlan,
            _requiresTrigramFallback,
            trigramExpression,
            rawQuery);
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
            NotNode notNode => $"NOT ({BuildMatch(notNode.Operand)})",
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
            _ => token.Value,
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

    private string BuildTrigramExpression()
    {
        if (_trigramExpressions.Count == 0)
        {
            return string.Empty;
        }

        if (_trigramExpressions.Count == 1)
        {
            return _trigramExpressions[0].Expression;
        }

        var builder = new StringBuilder();
        for (var index = 0; index < _trigramExpressions.Count; index++)
        {
            var current = _trigramExpressions[index];
            if (index > 0)
            {
                var op = current.RequireAll ? " AND " : " OR ";
                builder.Append(op);
            }

            builder.Append('(').Append(current.Expression).Append(')');
        }

        return builder.ToString();
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
