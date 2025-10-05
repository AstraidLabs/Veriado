namespace Veriado.Contracts.Search;

/// <summary>
/// Represents a saved search favourite exposed to the presentation layer.
/// </summary>
/// <param name="Id">The favourite identifier.</param>
/// <param name="Name">The unique favourite name.</param>
/// <param name="QueryText">The optional original query text.</param>
/// <param name="MatchQuery">The generated Lucene match query.</param>
/// <param name="Position">The ordering position.</param>
/// <param name="CreatedUtc">The creation timestamp.</param>
/// <param name="IsFuzzy">Indicates whether the favourite uses fuzzy search.</param>
public sealed record SearchFavoriteItem(
    Guid Id,
    string Name,
    string? QueryText,
    string MatchQuery,
    int Position,
    DateTimeOffset CreatedUtc,
    bool IsFuzzy);
