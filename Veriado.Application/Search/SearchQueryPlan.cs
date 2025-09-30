namespace Veriado.Appl.Search;

using System;
using Microsoft.Data.Sqlite;

/// <summary>
/// Represents a structured search query composed of FTS match clauses, range filters and scoring hints.
/// </summary>
/// <param name="MatchExpression">The SQLite FTS5 MATCH expression.</param>
/// <param name="WhereClauses">Additional SQL <c>WHERE</c> fragments joined using <c>AND</c>.</param>
/// <param name="Parameters">The parameters required by <see cref="WhereClauses"/>.</param>
/// <param name="ScorePlan">The scoring configuration.</param>
/// <param name="RequiresTrigramFallback">Indicates whether the query needs trigram evaluation.</param>
/// <param name="TrigramExpression">The optional trigram query expression.</param>
/// <param name="RawQueryText">The user supplied raw text for diagnostics.</param>
/// <param name="RequiresTrigramForWildcard">Indicates whether wildcard clauses require trigram evaluation.</param>
/// <param name="HasPrefix">Indicates whether the plan contains prefix terms.</param>
/// <param name="HasExplicitFuzzy">Indicates whether the query explicitly requested fuzzy matching.</param>
/// <param name="HasHeuristicFuzzy">Indicates whether fuzzy matching was applied heuristically.</param>
public sealed record SearchQueryPlan(
    string MatchExpression,
    IReadOnlyList<string> WhereClauses,
    IReadOnlyList<SqliteParameterDefinition> Parameters,
    SearchScorePlan ScorePlan,
    bool RequiresTrigramFallback,
    string? TrigramExpression,
    string? RawQueryText = null,
    bool RequiresTrigramForWildcard = false,
    bool HasPrefix = false,
    bool HasExplicitFuzzy = false,
    bool HasHeuristicFuzzy = false);

/// <summary>
/// Provides factory helpers for creating simple search plans.
/// </summary>
public static class SearchQueryPlanFactory
{
    /// <summary>
    /// Creates a plan representing a plain FTS5 match query without additional filters.
    /// </summary>
    public static SearchQueryPlan FromMatch(string matchExpression, string? rawQueryText = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matchExpression);
        return new SearchQueryPlan(
            matchExpression,
            Array.Empty<string>(),
            Array.Empty<SqliteParameterDefinition>(),
            new SearchScorePlan(),
            false,
            null,
            rawQueryText ?? matchExpression,
            false,
            false,
            false,
            false);
    }

    /// <summary>
    /// Creates a plan that executes only a trigram query.
    /// </summary>
    public static SearchQueryPlan FromTrigram(string trigramExpression, string? rawQueryText = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trigramExpression);
        return new SearchQueryPlan(
            string.Empty,
            Array.Empty<string>(),
            Array.Empty<SqliteParameterDefinition>(),
            new SearchScorePlan(),
            true,
            trigramExpression,
            rawQueryText ?? trigramExpression,
            false,
            false,
            false,
            false);
    }
}

/// <summary>
/// Describes a scoring configuration for an FTS5 query.
/// </summary>
public sealed record SearchScorePlan
{
    /// <summary>
    /// Initialises a new instance of the <see cref="SearchScorePlan"/> class.
    /// </summary>
    public SearchScorePlan()
    {
    }

    /// <summary>
    /// Gets or sets the BM25 weight applied to the title column.
    /// </summary>
    public double TitleWeight { get; set; } = 4.0d;

    /// <summary>
    /// Gets or sets the BM25 weight applied to the MIME column.
    /// </summary>
    public double MimeWeight { get; set; } = 0.1d;

    /// <summary>
    /// Gets or sets the BM25 weight applied to the author column.
    /// </summary>
    public double AuthorWeight { get; set; } = 2.0d;

    /// <summary>
    /// Gets or sets the BM25 weight applied to the metadata text column.
    /// </summary>
    public double MetadataTextWeight { get; set; } = 0.8d;

    /// <summary>
    /// Gets or sets the BM25 weight applied to the structured metadata JSON column.
    /// </summary>
    public double MetadataWeight { get; set; } = 0.2d;

    /// <summary>
    /// Gets or sets a value indicating whether the query should emit a TF-IDF like ranking value
    /// expressed as <c>1 / (damping + bm25)</c>.
    /// </summary>
    public bool UseTfIdfAlternative { get; set; }
        = false;

    /// <summary>
    /// Gets or sets the damping factor used when <see cref="UseTfIdfAlternative"/> is enabled.
    /// </summary>
    public double TfIdfDampingFactor { get; set; } = 0.5d;

    /// <summary>
    /// Gets or sets an optional score multiplier applied to the BM25 result.
    /// </summary>
    public double ScoreMultiplier { get; set; } = 1.0d;

    /// <summary>
    /// Gets or sets a value indicating whether larger rank values represent better matches.
    /// When <see langword="false"/>, lower scores are considered better (the default for BM25).
    /// </summary>
    public bool HigherScoreIsBetter { get; set; }
        = false;

    /// <summary>
    /// Gets or sets an optional SQL fragment that computes a custom rank.
    /// When specified the fragment must evaluate to a numeric value and can reference
    /// <c>bm25_score</c> (the computed bm25 value) as well as any table aliases present in the
    /// search query.
    /// </summary>
    public string? CustomRankExpression { get; set; }
        = null;

    /// <summary>
    /// Gets or sets an optional SQL fragment that computes a custom similarity contribution.
    /// The fragment is selected into the query as <c>custom_similarity</c>.
    /// </summary>
    public string? CustomSimilaritySql { get; set; }
        = null;

    /// <summary>
    /// Gets or sets an optional managed delegate invoked for each hit to compute a final similarity
    /// score. The delegate receives the raw BM25 score, the custom similarity value (when supplied)
    /// and the last modified timestamp.
    /// </summary>
    public Func<double, double?, DateTimeOffset?, double>? CustomSimilarityDelegate { get; set; }
        = null;
}

/// <summary>
/// Represents a SQLite parameter definition produced by the query builder.
/// </summary>
/// <param name="Name">The unique parameter name including the <c>$</c> prefix.</param>
/// <param name="Value">The parameter value.</param>
/// <param name="Type">The optional SQLite type hint.</param>
public sealed record SqliteParameterDefinition(string Name, object? Value, SqliteType? Type);
