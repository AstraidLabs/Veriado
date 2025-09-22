using System;

namespace Veriado.Domain.Search;

/// <summary>
/// Represents a stored search history entry.
/// </summary>
public sealed class SearchHistoryEntryEntity
{
    public Guid Id { get; set; }

    public string? QueryText { get; set; }

    public string Match { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    public int Executions { get; set; }

    public int? LastTotalHits { get; set; }

    public bool IsFuzzy { get; set; }
}
