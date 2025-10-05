namespace Veriado.Appl.Search;

using System.Collections.Generic;

/// <summary>
/// Represents a node in the Lucene query expression tree.
/// </summary>
public abstract record QueryNode;

/// <summary>
/// A node that wraps a literal term, phrase or proximity expression.
/// </summary>
/// <param name="Field">The optional field restriction.</param>
/// <param name="Value">The processed token value.</param>
/// <param name="TokenType">The token type.</param>
/// <param name="TrigramExpression">Optional trigram expression associated with the token.</param>
/// <param name="RequiresAllTrigramTerms">Indicates whether all trigram terms must match.</param>
public sealed record TokenNode(
    string? Field,
    string Value,
    QueryTokenType TokenType,
    string? TrigramExpression = null,
    bool RequiresAllTrigramTerms = false,
    int? MaxEditDistance = null,
    bool IsHeuristicFuzzy = false) : QueryNode;

/// <summary>
/// Represents a boolean combination of nodes.
/// </summary>
/// <param name="Operator">The boolean operator.</param>
/// <param name="Children">The child nodes.</param>
public sealed record BooleanNode(BooleanOperator Operator, IReadOnlyList<QueryNode> Children) : QueryNode;

/// <summary>
/// Represents a negated node.
/// </summary>
/// <param name="Operand">The operand.</param>
public sealed record NotNode(QueryNode Operand) : QueryNode;

/// <summary>
/// Enumerates the supported token types.
/// </summary>
public enum QueryTokenType
{
    /// <summary>
    /// A single token term.
    /// </summary>
    Term,

    /// <summary>
    /// An exact phrase.
    /// </summary>
    Phrase,

    /// <summary>
    /// A proximity expression constructed using NEAR.
    /// </summary>
    Proximity,

    /// <summary>
    /// A prefix match (trailing wildcard).
    /// </summary>
    Prefix,

    /// <summary>
    /// A token containing wildcard characters.
    /// </summary>
    Wildcard,

    /// <summary>
    /// A fuzzy token matched using trigram similarity.
    /// </summary>
    Fuzzy,
}

/// <summary>
/// Enumerates the supported boolean operators.
/// </summary>
public enum BooleanOperator
{
    /// <summary>
    /// Logical AND.
    /// </summary>
    And,

    /// <summary>
    /// Logical OR.
    /// </summary>
    Or,
}
