namespace Veriado.Models.Search;

/// <summary>
/// Represents the data required to create a new search favourite from the UI.
/// </summary>
public sealed class SearchFavoriteDefinitionModel
{
    public string Name { get; init; } = string.Empty;

    public string MatchQuery { get; init; } = string.Empty;

    public string? QueryText { get; init; }

    public bool IsFuzzy { get; init; }
}
