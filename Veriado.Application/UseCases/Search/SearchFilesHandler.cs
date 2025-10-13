using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AutoMapper;
using Veriado.Appl.Search;
using Veriado.Appl.Search.Abstractions;

namespace Veriado.Appl.UseCases.Search;

/// <summary>
/// Handles full-text search queries for files.
/// </summary>
public sealed class SearchFilesHandler : IRequestHandler<SearchFilesQuery, IReadOnlyList<SearchHitDto>>
{
    private readonly ISearchQueryService _searchQueryService;
    private readonly IMapper _mapper;
    private readonly IAnalyzerFactory _analyzerFactory;
    private readonly SearchOptions _searchOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchFilesHandler"/> class.
    /// </summary>
    public SearchFilesHandler(
        ISearchQueryService searchQueryService,
        IMapper mapper,
        IAnalyzerFactory analyzerFactory,
        SearchOptions searchOptions)
    {
        _searchQueryService = searchQueryService;
        _mapper = mapper;
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
        _searchOptions = searchOptions ?? throw new ArgumentNullException(nameof(searchOptions));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchHitDto>> Handle(SearchFilesQuery request, CancellationToken cancellationToken)
    {
        Guard.AgainstNullOrWhiteSpace(request.Text, nameof(request.Text));
        var builder = new SearchQueryBuilder(_searchOptions.Score);
        var expression = BuildQueryExpression(request.Text, builder);
        if (expression is null)
        {
            return Array.Empty<SearchHitDto>();
        }

        var plan = builder.Build(expression, request.Text);
        var hits = await _searchQueryService.SearchAsync(plan, request.Limit, cancellationToken);
        return _mapper.Map<IReadOnlyList<SearchHitDto>>(hits);
    }

    private QueryNode? BuildQueryExpression(string text, SearchQueryBuilder builder)
    {
        var lexTokens = Lex(text);
        if (lexTokens.Count == 0)
        {
            return null;
        }

        var parseTokens = new List<ParseToken>(lexTokens.Count);
        string? pendingField = null;
        ParseToken? previousToken = null;

        foreach (var token in lexTokens)
        {
            switch (token.Type)
            {
                case LexTokenType.Field:
                    pendingField = token.Value;
                    break;
                case LexTokenType.Operator:
                    pendingField = null;
                    AddToken(ParseToken.Operator(token.OperatorKind));
                    break;
                case LexTokenType.Phrase:
                    if (CreatePhraseNode(token.Value, pendingField) is { } phraseNode)
                    {
                        AddToken(ParseToken.Node(phraseNode));
                    }

                    pendingField = null;
                    break;
                case LexTokenType.Word:
                    var nodes = CreateTermNodes(token.Value, pendingField);
                    pendingField = null;
                    foreach (var node in nodes)
                    {
                        AddToken(ParseToken.Node(node));
                    }

                    break;
                case LexTokenType.OpenParen:
                    AddToken(ParseToken.OpenParen());
                    break;
                case LexTokenType.CloseParen:
                    AddToken(ParseToken.CloseParen());
                    break;
            }
        }

        return BuildBooleanTree(builder, parseTokens);

        void AddToken(ParseToken token)
        {
            if (pendingField is not null && token.Type != ParseTokenType.Node)
            {
                pendingField = null;
            }

            if (previousToken is { } previous && RequiresImplicitAnd(previous, token))
            {
                parseTokens.Add(ParseToken.Operator(ParseTokenType.And));
            }

            parseTokens.Add(token);
            previousToken = token;
        }
    }

    private IReadOnlyList<LexToken> Lex(string text)
    {
        var tokens = new List<LexToken>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return tokens;
        }

        var span = text.AsSpan();
        var builder = new StringBuilder();
        var index = 0;

        while (index < span.Length)
        {
            var current = span[index];
            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            if (current == '(')
            {
                tokens.Add(LexToken.OpenParen());
                index++;
                continue;
            }

            if (current == ')')
            {
                tokens.Add(LexToken.CloseParen());
                index++;
                continue;
            }

            if (current == '\"')
            {
                index++;
                builder.Clear();
                var closed = false;

                while (index < span.Length)
                {
                    var ch = span[index];
                    if (ch == '\"')
                    {
                        if (index + 1 < span.Length && span[index + 1] == '\"')
                        {
                            builder.Append('\"');
                            index += 2;
                            continue;
                        }

                        closed = true;
                        index++;
                        break;
                    }

                    builder.Append(ch);
                    index++;
                }

                if (!closed)
                {
                    index = span.Length;
                }

                if (builder.Length > 0)
                {
                    tokens.Add(LexToken.Phrase(builder.ToString()));
                }

                continue;
            }

            var start = index;
            while (index < span.Length
                && !char.IsWhiteSpace(span[index])
                && span[index] != '\"'
                && span[index] != '('
                && span[index] != ')')
            {
                index++;
            }

            if (start == index)
            {
                index++;
                continue;
            }

            var raw = span.Slice(start, index - start).ToString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            ProcessRawToken(raw);
        }

        return tokens;

        void ProcessRawToken(string rawToken)
        {
            if (string.IsNullOrWhiteSpace(rawToken))
            {
                return;
            }

            if (rawToken.Equals("AND", StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(LexToken.Operator(ParseTokenType.And));
                return;
            }

            if (rawToken.Equals("OR", StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(LexToken.Operator(ParseTokenType.Or));
                return;
            }

            if (rawToken.Equals("NOT", StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(LexToken.Operator(ParseTokenType.Not));
                return;
            }

            if (TryExtractField(rawToken, out var fieldName, out var remainder))
            {
                tokens.Add(LexToken.Field(fieldName));
                if (!string.IsNullOrEmpty(remainder))
                {
                    foreach (var nested in Lex(remainder))
                    {
                        tokens.Add(nested);
                    }
                }

                return;
            }

            tokens.Add(LexToken.Word(rawToken));
        }
    }

    private IEnumerable<QueryNode> CreateTermNodes(string text, string? field)
    {
        var descriptor = AnalyzeToken(text);
        switch (descriptor.Classification)
        {
            case QueryTokenType.Prefix:
                if (CreatePrefixNode(descriptor, field) is { } prefixNode)
                {
                    yield return prefixNode;
                }

                yield break;
            case QueryTokenType.Wildcard:
                if (CreateWildcardNode(descriptor, field) is { } wildcardNode)
                {
                    yield return wildcardNode;
                }

                yield break;
        }

        var tokens = TextNormalization.Tokenize(descriptor.Sanitized, _analyzerFactory)
            .Where(static token => !string.IsNullOrWhiteSpace(token))
            .ToArray();

        if (tokens.Length == 0)
        {
            yield break;
        }

        foreach (var token in tokens)
        {
            var escaped = TextNormalization.EscapeMatchToken(token);
            if (string.IsNullOrWhiteSpace(escaped))
            {
                continue;
            }

            if (IsReservedWord(token))
            {
                yield return new TokenNode(field, escaped, QueryTokenType.Phrase);
                continue;
            }

            yield return new TokenNode(field, escaped, QueryTokenType.Term);
        }
    }

    private QueryNode? CreatePhraseNode(string text, string? field)
    {
        var tokens = TextNormalization.Tokenize(text, _analyzerFactory)
            .Where(static token => !string.IsNullOrWhiteSpace(token))
            .Select(TextNormalization.EscapeMatchToken)
            .Where(static token => !string.IsNullOrWhiteSpace(token))
            .ToArray();

        if (tokens.Length == 0)
        {
            return null;
        }

        var value = string.Join(' ', tokens);
        return new TokenNode(field, value, QueryTokenType.Phrase);
    }

    private QueryNode? BuildBooleanTree(SearchQueryBuilder builder, IReadOnlyList<ParseToken> tokens)
    {
        if (tokens.Count == 0)
        {
            return null;
        }

        var nodeStack = new Stack<QueryNode>();
        var operatorStack = new Stack<ParseTokenType>();

        foreach (var token in tokens)
        {
            switch (token.Type)
            {
                case ParseTokenType.Node:
                    nodeStack.Push(token.NodeValue!);
                    break;
                case ParseTokenType.Not:
                    operatorStack.Push(ParseTokenType.Not);
                    break;
                case ParseTokenType.And:
                case ParseTokenType.Or:
                    while (operatorStack.Count > 0 && ShouldCollapse(operatorStack.Peek(), token.Type))
                    {
                        ApplyOperator(operatorStack.Pop());
                    }

                    operatorStack.Push(token.Type);
                    break;
                case ParseTokenType.OpenParen:
                    operatorStack.Push(ParseTokenType.OpenParen);
                    break;
                case ParseTokenType.CloseParen:
                    while (operatorStack.Count > 0 && operatorStack.Peek() != ParseTokenType.OpenParen)
                    {
                        ApplyOperator(operatorStack.Pop());
                    }

                    if (operatorStack.Count > 0 && operatorStack.Peek() == ParseTokenType.OpenParen)
                    {
                        operatorStack.Pop();
                    }

                    break;
            }
        }

        while (operatorStack.Count > 0)
        {
            var op = operatorStack.Pop();
            if (op == ParseTokenType.OpenParen)
            {
                continue;
            }

            ApplyOperator(op);
        }

        return nodeStack.Count == 0 ? null : nodeStack.Pop();

        bool ShouldCollapse(ParseTokenType opOnStack, ParseTokenType incoming)
        {
            if (opOnStack == ParseTokenType.OpenParen)
            {
                return false;
            }

            var stackPrecedence = GetPrecedence(opOnStack);
            var incomingPrecedence = GetPrecedence(incoming);
            if (opOnStack == ParseTokenType.Not && incomingPrecedence == GetPrecedence(ParseTokenType.Not))
            {
                return false;
            }

            return stackPrecedence >= incomingPrecedence;
        }

        void ApplyOperator(ParseTokenType op)
        {
            if (op == ParseTokenType.Not)
            {
                if (nodeStack.Count == 0)
                {
                    return;
                }

                var operand = nodeStack.Pop();
                nodeStack.Push(new NotNode(operand));
                return;
            }

            if (nodeStack.Count < 2)
            {
                return;
            }

            var right = nodeStack.Pop();
            var left = nodeStack.Pop();
            var combined = op == ParseTokenType.And
                ? builder.And(left, right)
                : builder.Or(left, right);

            if (combined is not null)
            {
                nodeStack.Push(combined);
            }
        }
    }

    private static bool RequiresImplicitAnd(ParseToken previous, ParseToken next)
    {
        if (previous.Type is ParseTokenType.Node or ParseTokenType.CloseParen)
        {
            return next.Type is ParseTokenType.Node or ParseTokenType.OpenParen or ParseTokenType.Not;
        }

        return false;
    }

    private TokenDescriptor AnalyzeToken(string raw)
    {
        var trimmed = raw?.Trim() ?? string.Empty;
        var descriptor = new TokenDescriptor(trimmed, trimmed, QueryTokenType.Term);
        if (trimmed.Length == 0)
        {
            return descriptor;
        }

        var hasQuestion = trimmed.IndexOf('?', StringComparison.Ordinal) >= 0;
        var firstAsterisk = trimmed.IndexOf('*', StringComparison.Ordinal);
        if (firstAsterisk >= 0)
        {
            var lastAsterisk = trimmed.LastIndexOf('*');
            var trailingOnly = firstAsterisk == lastAsterisk && lastAsterisk == trimmed.Length - 1;
            if (trailingOnly && !hasQuestion)
            {
                descriptor = descriptor with
                {
                    Sanitized = trimmed[..^1],
                    Classification = QueryTokenType.Prefix,
                };
            }
            else
            {
                descriptor = descriptor with
                {
                    Sanitized = trimmed,
                    Classification = QueryTokenType.Wildcard,
                };
            }

            return descriptor;
        }

        if (hasQuestion)
        {
            descriptor = descriptor with
            {
                Sanitized = trimmed,
                Classification = QueryTokenType.Wildcard,
            };
        }

        return descriptor;
    }

    private QueryNode? CreatePrefixNode(TokenDescriptor descriptor, string? field)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Sanitized))
        {
            return null;
        }

        var normalized = NormalizeSingleToken(descriptor.Sanitized);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var escaped = TextNormalization.EscapeMatchToken(normalized);
        if (string.IsNullOrWhiteSpace(escaped))
        {
            return null;
        }

        return new TokenNode(field, escaped + '*', QueryTokenType.Prefix);
    }

    private QueryNode? CreateWildcardNode(TokenDescriptor descriptor, string? field)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Sanitized))
        {
            return null;
        }

        var normalized = NormalizeWildcardToken(descriptor.Sanitized);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var escaped = TextNormalization.EscapeMatchToken(normalized);
        if (string.IsNullOrWhiteSpace(escaped))
        {
            return null;
        }

        return new TokenNode(field, escaped, QueryTokenType.Wildcard);
    }

    private string NormalizeSingleToken(string token)
    {
        var tokens = TextNormalization.Tokenize(token, _analyzerFactory)
            .Where(static part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        if (tokens.Length == 0)
        {
            return string.Empty;
        }

        return tokens.Length == 1 ? tokens[0] : string.Join(' ', tokens);
    }

    private string NormalizeWildcardToken(string token)
    {
        var builder = new StringBuilder(token.Length);
        var segment = new StringBuilder();

        foreach (var ch in token)
        {
            if (ch is '*' or '?')
            {
                AppendSegment();
                builder.Append(ch);
            }
            else
            {
                segment.Append(ch);
            }
        }

        AppendSegment();
        return builder.ToString();

        void AppendSegment()
        {
            if (segment.Length == 0)
            {
                return;
            }

            var normalized = NormalizeWildcardSegment(segment.ToString());
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                builder.Append(normalized);
            }

            segment.Clear();
        }
    }

    private string NormalizeWildcardSegment(string segment)
    {
        var tokens = TextNormalization.Tokenize(segment, _analyzerFactory)
            .Where(static part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        if (tokens.Length == 0)
        {
            return string.Empty;
        }

        return string.Concat(tokens);
    }

    private static bool TryExtractField(string rawToken, out string fieldName, out string remainder)
    {
        fieldName = string.Empty;
        remainder = string.Empty;

        var separatorIndex = rawToken.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return false;
        }

        var candidate = rawToken[..separatorIndex];
        if (!FieldQualifiers.Contains(candidate))
        {
            return false;
        }

        fieldName = candidate.ToLowerInvariant();
        remainder = separatorIndex + 1 < rawToken.Length ? rawToken[(separatorIndex + 1)..] : string.Empty;
        return true;
    }

    private static int GetPrecedence(ParseTokenType type)
        => type switch
        {
            ParseTokenType.Not => 3,
            ParseTokenType.And => 2,
            ParseTokenType.Or => 1,
            _ => 0,
        };
    private static bool IsReservedWord(string token)
        => ReservedWords.Contains(token);

    private static readonly HashSet<string> ReservedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "and",
        "or",
        "not",
        "near",
    };

    private static readonly HashSet<string> FieldQualifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "title",
        "author",
        "mime",
        "metadata_text",
    };

    private enum LexTokenType
    {
        Word,
        Phrase,
        Operator,
        Field,
        OpenParen,
        CloseParen,
    }

    private enum ParseTokenType
    {
        Node,
        And,
        Or,
        Not,
        OpenParen,
        CloseParen,
    }

    private readonly record struct LexToken(LexTokenType Type, string Value, ParseTokenType OperatorKind)
    {
        public static LexToken Word(string value)
            => new(LexTokenType.Word, value, ParseTokenType.Node);

        public static LexToken Phrase(string value)
            => new(LexTokenType.Phrase, value, ParseTokenType.Node);

        public static LexToken Field(string value)
            => new(LexTokenType.Field, value, ParseTokenType.Node);

        public static LexToken Operator(ParseTokenType op)
            => new(LexTokenType.Operator, string.Empty, op);

        public static LexToken OpenParen()
            => new(LexTokenType.OpenParen, string.Empty, ParseTokenType.OpenParen);

        public static LexToken CloseParen()
            => new(LexTokenType.CloseParen, string.Empty, ParseTokenType.CloseParen);
    }

    private readonly record struct ParseToken(ParseTokenType Type, QueryNode? NodeValue)
    {
        public static ParseToken Node(QueryNode node)
            => new(ParseTokenType.Node, node);

        public static ParseToken Operator(ParseTokenType op)
            => new(op, null);

        public static ParseToken OpenParen()
            => new(ParseTokenType.OpenParen, null);

        public static ParseToken CloseParen()
            => new(ParseTokenType.CloseParen, null);
    }

    private readonly record struct TokenDescriptor(
        string Original,
        string Sanitized,
        QueryTokenType Classification);
}
