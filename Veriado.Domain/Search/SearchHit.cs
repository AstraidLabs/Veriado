using System.Collections.Generic;

namespace Veriado.Domain.Search;

/// <summary>
/// Represents a highlighted segment within a search hit snippet.
/// </summary>
/// <param name="Field">The field containing the highlighted text.</param>
/// <param name="Start">The zero-based character offset within the snippet.</param>
/// <param name="Length">The length of the highlighted segment in characters.</param>
/// <param name="Term">The original matching term, when available.</param>
public sealed record HighlightSpan(string Field, int Start, int Length, string? Term);

/// <summary>
/// Represents a unified search hit returned by the search subsystem.
/// </summary>
/// <param name="Id">The unique identifier of the matching document.</param>
/// <param name="Score">The relevance score for the hit.</param>
/// <param name="PrimaryField">The primary field used to generate the snippet.</param>
/// <param name="SnippetText">The snippet presented to the caller.</param>
/// <param name="Highlights">The highlight spans contained within the snippet.</param>
/// <param name="Fields">Additional indexed fields associated with the hit.</param>
/// <param name="SortValues">Optional sort metadata emitted by the query pipeline.</param>
public sealed record SearchHit(
    Guid Id,
    double Score,
    string? PrimaryField,
    string SnippetText,
    List<HighlightSpan> Highlights,
    Dictionary<string, string?> Fields,
    object? SortValues);

/// <summary>
/// Provides optional sort metadata emitted with a search hit.
/// </summary>
/// <param name="LastModifiedUtc">The last modification timestamp of the document.</param>
/// <param name="NormalizedScore">A score normalised to the range &lt;0,1&gt; for ordering.</param>
/// <param name="RawScore">The raw score produced by the underlying search provider.</param>
public sealed record SearchHitSortValues(
    DateTimeOffset LastModifiedUtc,
    double NormalizedScore,
    double RawScore);
