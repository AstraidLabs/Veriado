namespace Veriado.Services.Files.Models;

/// <summary>
/// Represents the information required to create a saved search favourite.
/// </summary>
public sealed record SearchFavoriteDefinition(string Name, string MatchQuery, string? QueryText, bool IsFuzzy);
