using System;

namespace Veriado.WinUI.Models.Search;

/// <summary>
/// Represents a single entry in the user search history.
/// </summary>
public sealed class SearchHistoryItemModel
{
    public Guid Id { get; init; }

    public string? QueryText { get; init; }

    public string MatchQuery { get; init; } = string.Empty;

    public DateTimeOffset LastQueriedUtc { get; init; }

    public int Executions { get; init; }

    public int? LastTotalHits { get; init; }

    public bool IsFuzzy { get; init; }
}
