namespace Veriado.Domain.Search;

/// <summary>
/// Represents an autocomplete suggestion token harvested from indexed documents.
/// </summary>
public sealed class SuggestionEntry
{
    /// <summary>
    /// Gets the surrogate identifier for the suggestion entry.
    /// </summary>
    public long Id { get; private set; }

    /// <summary>
    /// Gets the suggestion term.
    /// </summary>
    public string Term { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the weighted score associated with the term.
    /// </summary>
    public double Weight { get; private set; }

    /// <summary>
    /// Gets the language associated with the suggestion.
    /// </summary>
    public string Language { get; private set; } = "en";

    /// <summary>
    /// Gets the originating field name used when harvesting.
    /// </summary>
    public string SourceField { get; private set; } = string.Empty;
}
