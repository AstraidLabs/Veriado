namespace Veriado.Appl.Search;

using System;

/// <summary>
/// Provides a fluent API for constructing rich SQLite FTS5 search queries including range filters
/// and scoring metadata.
/// </summary>
public interface ISearchQueryBuilder
{
    /// <summary>
    /// Creates a term node matching the supplied token.
    /// </summary>
    /// <param name="field">An optional field restriction.</param>
    /// <param name="term">The term to match.</param>
    /// <returns>The created query node or <see langword="null"/> when no valid token could be produced.</returns>
    QueryNode? Term(string? field, string term);

    /// <summary>
    /// Creates a phrase node that requires an exact match.
    /// </summary>
    /// <param name="field">The column restriction.</param>
    /// <param name="phrase">The phrase to match.</param>
    /// <returns>The created query node or <see langword="null"/> when no valid token could be produced.</returns>
    QueryNode? Phrase(string? field, string phrase);

    /// <summary>
    /// Creates a proximity node ensuring the two supplied tokens appear within the specified distance.
    /// </summary>
    /// <param name="field">The column restriction.</param>
    /// <param name="first">The first token.</param>
    /// <param name="second">The second token.</param>
    /// <param name="distance">The maximum distance between the tokens.</param>
    /// <returns>The created query node or <see langword="null"/> when no valid token could be produced.</returns>
    QueryNode? Proximity(string? field, string first, string second, int distance);

    /// <summary>
    /// Creates a prefix match node.
    /// </summary>
    /// <param name="field">The column restriction.</param>
    /// <param name="prefix">The prefix including the trailing wildcard.</param>
    /// <returns>The created query node or <see langword="null"/> when no valid token could be produced.</returns>
    QueryNode? Prefix(string? field, string prefix);

    /// <summary>
    /// Signals that the provided token should be resolved using trigram similarity instead of FTS5.
    /// </summary>
    /// <param name="field">The optional column restriction used for diagnostics.</param>
    /// <param name="term">The fuzzy term.</param>
    /// <param name="requireAllTerms">Indicates whether all trigram terms should match.</param>
    /// <returns>An optional FTS node to combine with other clauses.</returns>
    QueryNode? Fuzzy(string? field, string term, bool requireAllTerms = false);

    /// <summary>
    /// Combines the supplied nodes using the logical AND operator.
    /// </summary>
    /// <param name="nodes">The child nodes.</param>
    /// <returns>The combined node or <see langword="null"/> when no children were provided.</returns>
    QueryNode? And(params QueryNode?[] nodes);

    /// <summary>
    /// Combines the supplied nodes using the logical OR operator.
    /// </summary>
    /// <param name="nodes">The child nodes.</param>
    /// <returns>The combined node or <see langword="null"/> when no children were provided.</returns>
    QueryNode? Or(params QueryNode?[] nodes);

    /// <summary>
    /// Negates the supplied node.
    /// </summary>
    /// <param name="node">The node to negate.</param>
    /// <returns>The negated node or <see langword="null"/> when the argument was <see langword="null"/>.</returns>
    QueryNode? Not(QueryNode? node);

    /// <summary>
    /// Adds a range filter that will be applied in the SQL WHERE clause.
    /// </summary>
    /// <param name="field">The field identifier.</param>
    /// <param name="from">The lower bound.</param>
    /// <param name="to">The upper bound.</param>
    /// <param name="includeLower">Indicates whether the lower bound is inclusive.</param>
    /// <param name="includeUpper">Indicates whether the upper bound is inclusive.</param>
    void Range(string field, object? from, object? to, bool includeLower = true, bool includeUpper = true);

    /// <summary>
    /// Applies a boost factor to the specified column by adjusting the BM25 weight.
    /// </summary>
    /// <param name="field">The column identifier.</param>
    /// <param name="factor">The multiplier applied to the default weight.</param>
    void Boost(string field, double factor);

    /// <summary>
    /// Enables a TF-IDF like score expression expressed as <c>1 / (d + bm25)</c>.
    /// </summary>
    /// <param name="dampingFactor">The damping factor.</param>
    void UseTfIdfRanking(double dampingFactor = 0.5d);

    /// <summary>
    /// Overrides the SQL rank expression used for ordering.
    /// </summary>
    /// <param name="sqlExpression">The SQL fragment.</param>
    /// <param name="higherIsBetter">Indicates whether larger values represent better matches.</param>
    void UseRankExpression(string sqlExpression, bool higherIsBetter = false);

    /// <summary>
    /// Supplies a SQL expression that yields an additional similarity value.
    /// </summary>
    /// <param name="sqlExpression">The SQL fragment.</param>
    void UseCustomSimilaritySql(string sqlExpression);

    /// <summary>
    /// Registers a managed delegate that computes a final similarity score.
    /// </summary>
    /// <param name="similarity">The delegate.</param>
    void UseCustomSimilarity(Func<double, double?, DateTimeOffset?, double> similarity);

    /// <summary>
    /// Constructs the search query plan from the provided root node.
    /// </summary>
    /// <param name="root">The root node.</param>
    /// <param name="rawQuery">The optional raw query text.</param>
    /// <returns>The built search query plan.</returns>
    SearchQueryPlan Build(QueryNode? root, string? rawQuery = null);
}
