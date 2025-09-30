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
}
