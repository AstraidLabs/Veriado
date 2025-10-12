namespace Veriado.Appl.Search;

/// <summary>
/// Provides configuration for the search query parser.
/// </summary>
public sealed class SearchParseOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether heuristic fuzzy detection is enabled.
    /// The flag is currently unused because only FTS5 queries are generated.
    /// </summary>
    public bool EnableHeuristicFuzzy { get; set; } = false;

    /// <summary>
    /// Retained for backwards compatibility. No longer influences query planning because
    /// trigram fallbacks have been removed.
    /// </summary>
    public int PrefixMinResults { get; set; } = 3;

    /// <summary>
    /// Retained for backwards compatibility. No longer influences query planning because
    /// trigram fallbacks have been removed.
    /// </summary>
    public int FuzzyMinResults { get; set; } = 5;

    /// <summary>
    /// Retained for backwards compatibility. No longer influences query planning because
    /// trigram fallbacks have been removed.
    /// </summary>
    public double FuzzyScoreThreshold { get; set; } = 0.45d;
}
