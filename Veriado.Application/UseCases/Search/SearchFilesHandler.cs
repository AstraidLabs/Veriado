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

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchFilesHandler"/> class.
    /// </summary>
    public SearchFilesHandler(ISearchQueryService searchQueryService, IMapper mapper, IAnalyzerFactory analyzerFactory)
    {
        _searchQueryService = searchQueryService;
        _mapper = mapper;
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchHitDto>> Handle(SearchFilesQuery request, CancellationToken cancellationToken)
    {
        Guard.AgainstNullOrWhiteSpace(request.Text, nameof(request.Text));
        var builder = new SearchQueryBuilder();
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

        var syntaxTokens = new List<SyntaxToken>(lexTokens.Count);
        foreach (var token in lexTokens)
        {
            switch (token.Type)
            {
                case LexTokenType.Operator:
                    syntaxTokens.Add(SyntaxToken.Operator(token.Operator));
                    break;
                case LexTokenType.Phrase:
                    if (CreatePhraseNode(token.Value) is { } phraseNode)
                    {
                        syntaxTokens.Add(SyntaxToken.Node(phraseNode));
                    }

                    break;
                case LexTokenType.Word:
                    foreach (var node in CreateTermNodes(token.Value))
                    {
                        syntaxTokens.Add(SyntaxToken.Node(node));
                    }

                    break;
            }
        }

        if (syntaxTokens.Count == 0)
        {
            return null;
        }

        return BuildBooleanTree(builder, syntaxTokens);
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
            while (index < span.Length && !char.IsWhiteSpace(span[index]) && span[index] != '\"')
            {
                index++;
            }

            if (start == index)
            {
                index++;
                continue;
            }

            var raw = span.Slice(start, index - start).ToString();
            if (raw.Length == 0)
            {
                continue;
            }

            if (raw.Equals("AND", StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(LexToken.Operator(SyntaxTokenType.And));
            }
            else if (raw.Equals("OR", StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(LexToken.Operator(SyntaxTokenType.Or));
            }
            else if (raw.Equals("NOT", StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(LexToken.Operator(SyntaxTokenType.Not));
            }
            else
            {
                tokens.Add(LexToken.Word(raw));
            }
        }

        return tokens;
    }

    private IEnumerable<QueryNode> CreateTermNodes(string text)
    {
        var tokens = TextNormalization.Tokenize(text, _analyzerFactory)
            .Where(static token => !string.IsNullOrWhiteSpace(token))
            .ToArray();

        foreach (var token in tokens)
        {
            var escaped = TextNormalization.EscapeMatchToken(token);
            if (string.IsNullOrWhiteSpace(escaped))
            {
                continue;
            }

            yield return IsReservedWord(token)
                ? new TokenNode(null, escaped, QueryTokenType.Phrase)
                : new TokenNode(null, escaped, QueryTokenType.Term);
        }
    }

    private QueryNode? CreatePhraseNode(string text)
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
        return new TokenNode(null, value, QueryTokenType.Phrase);
    }

    private QueryNode? BuildBooleanTree(SearchQueryBuilder builder, IReadOnlyList<SyntaxToken> tokens)
    {
        if (tokens.Count == 0)
        {
            return null;
        }

        var orClauses = new List<QueryNode>();
        var currentAnd = new List<QueryNode>();
        SyntaxTokenType? pendingOperator = null;
        BooleanOperator? lastExplicitOperator = null;
        var pendingNot = 0;

        foreach (var token in tokens)
        {
            switch (token.Type)
            {
                case SyntaxTokenType.Node:
                    var node = token.Node!;
                    if (pendingNot % 2 != 0)
                    {
                        node = new NotNode(node);
                    }

                    pendingNot = 0;

                    if (pendingOperator == SyntaxTokenType.And)
                    {
                        currentAnd.Add(node);
                    }
                    else if (pendingOperator == SyntaxTokenType.Or)
                    {
                        FinalizeCurrentAnd();
                        currentAnd.Add(node);
                    }
                    else if (currentAnd.Count == 0)
                    {
                        currentAnd.Add(node);
                    }
                    else if (lastExplicitOperator == BooleanOperator.And)
                    {
                        currentAnd.Add(node);
                    }
                    else
                    {
                        FinalizeCurrentAnd();
                        currentAnd.Add(node);
                    }

                    pendingOperator = null;
                    break;
                case SyntaxTokenType.And:
                    pendingOperator = SyntaxTokenType.And;
                    lastExplicitOperator = BooleanOperator.And;
                    break;
                case SyntaxTokenType.Or:
                    pendingOperator = SyntaxTokenType.Or;
                    lastExplicitOperator = BooleanOperator.Or;
                    break;
                case SyntaxTokenType.Not:
                    pendingNot++;
                    break;
            }
        }

        FinalizeCurrentAnd();

        if (orClauses.Count == 0)
        {
            return null;
        }

        return orClauses.Count == 1
            ? orClauses[0]
            : builder.Or(orClauses.ToArray());

        void FinalizeCurrentAnd()
        {
            if (currentAnd.Count == 0)
            {
                return;
            }

            QueryNode? node = currentAnd.Count == 1
                ? currentAnd[0]
                : builder.And(currentAnd.ToArray());

            if (node is not null)
            {
                orClauses.Add(node);
            }

            currentAnd.Clear();
        }
    }

    private static bool IsReservedWord(string token)
        => ReservedWords.Contains(token);

    private static readonly HashSet<string> ReservedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "and",
        "or",
        "not",
        "near",
    };

    private enum LexTokenType
    {
        Word,
        Phrase,
        Operator,
    }

    private enum SyntaxTokenType
    {
        Node,
        And,
        Or,
        Not,
    }

    private readonly record struct LexToken(LexTokenType Type, string Value, SyntaxTokenType Operator)
    {
        public static LexToken Word(string value)
            => new(LexTokenType.Word, value, SyntaxTokenType.Node);

        public static LexToken Phrase(string value)
            => new(LexTokenType.Phrase, value, SyntaxTokenType.Node);

        public static LexToken Operator(SyntaxTokenType op)
            => new(LexTokenType.Operator, string.Empty, op);
    }

    private readonly record struct SyntaxToken(SyntaxTokenType Type, QueryNode? Node)
    {
        public static SyntaxToken Node(QueryNode node)
            => new(SyntaxTokenType.Node, node);

        public static SyntaxToken Operator(SyntaxTokenType op)
            => new(op, null);
    }
}
