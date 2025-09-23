using System;

namespace Veriado.WinUI.Models.Search;

/// <summary>
/// Represents a single search result displayed in the UI.
/// </summary>
public sealed class SearchHitModel
{
    public Guid FileId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Mime { get; init; } = string.Empty;

    public string? Snippet { get; init; }

    public double Score { get; init; }

    public DateTimeOffset LastModifiedUtc { get; init; }
}
