using System;

namespace Veriado.Infrastructure.Search.Entities;

public sealed class SearchFavoriteEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? QueryText { get; set; }

    public string Match { get; set; } = string.Empty;

    public int Position { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public bool IsFuzzy { get; set; }
}
