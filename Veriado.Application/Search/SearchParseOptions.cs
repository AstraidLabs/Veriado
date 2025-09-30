namespace Veriado.Appl.Search;

/// <summary>
/// Provides configuration for the search query parser.
/// </summary>
public sealed class SearchParseOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether heuristic fuzzy detection is enabled.
    /// </summary>
    public bool EnableHeuristicFuzzy { get; set; } = true;

    /// <summary>
    /// Gets or sets the minimum number of FTS hits required for prefix queries to avoid trigram fallback.
    /// </summary>
    public int PrefixMinResults { get; set; } = 3;

    /// <summary>
    /// Gets or sets the minimum number of FTS hits required for fuzzy queries to avoid trigram fallback.
    /// </summary>
    public int FuzzyMinResults { get; set; } = 5;

    /// <summary>
    /// Gets or sets the minimum top normalized score required for fuzzy queries to avoid trigram fallback.
    /// </summary>
    public double FuzzyScoreThreshold { get; set; } = 0.45d;
}
