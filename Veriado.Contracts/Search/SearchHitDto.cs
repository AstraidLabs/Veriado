using System.Collections.Generic;

namespace Veriado.Contracts.Search;

/// <summary>
/// Represents an individual highlight span inside a snippet.
/// </summary>
/// <param name="Field">The field associated with the highlight.</param>
/// <param name="Start">The zero-based start offset inside the snippet.</param>
/// <param name="Length">The number of characters covered by the highlight.</param>
/// <param name="Term">The optional matching term.</param>
public sealed record HighlightSpanDto(string Field, int Start, int Length, string? Term);

/// <summary>
/// Represents a single hit returned by the search subsystem.
/// </summary>
/// <param name="Id">The identifier of the matching file.</param>
/// <param name="Score">The relevance score.</param>
/// <param name="Source">The origin of the hit (FTS or TRIGRAM).</param>
/// <param name="PrimaryField">The field from which the snippet was generated.</param>
/// <param name="SnippetText">The plain-text snippet.</param>
/// <param name="Highlights">The highlight spans located within the snippet.</param>
/// <param name="Fields">Additional key/value fields related to the hit.</param>
/// <param name="SortValues">Optional sort metadata.</param>
public sealed record SearchHitDto(
    Guid Id,
    double Score,
    string Source,
    string? PrimaryField,
    string SnippetText,
    List<HighlightSpanDto> Highlights,
    Dictionary<string, string?> Fields,
    object? SortValues);
