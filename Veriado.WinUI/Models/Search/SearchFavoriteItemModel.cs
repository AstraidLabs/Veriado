using System;

namespace Veriado.Models.Search;

/// <summary>
/// Represents a saved favourite search entry exposed in the UI.
/// </summary>
public sealed class SearchFavoriteItemModel
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? QueryText { get; init; }

    public string MatchQuery { get; init; } = string.Empty;

    public int Position { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }

    public bool IsFuzzy { get; init; }
}
