namespace Veriado.Contracts.Search;

/// <summary>
/// Represents the information required to create a saved search favourite.
/// </summary>
/// <param name="Name">The unique display name assigned to the favourite.</param>
/// <param name="MatchQuery">The generated FTS5 match query expression.</param>
/// <param name="QueryText">The original user supplied query text.</param>
public sealed record SearchFavoriteDefinition(
    string Name,
    string MatchQuery,
    string? QueryText);
