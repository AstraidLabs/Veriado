using System.Collections.Generic;
using Veriado.Contracts.Files;

namespace Veriado.Appl.Search;

/// <summary>
/// Represents the materialised result returned by the centralised grid search query.
/// </summary>
public sealed record FileGridSearchResult(
    IReadOnlyList<FileSummaryDto> Items,
    int TotalCount,
    bool HasMore)
{
    /// <summary>
    /// Gets an empty result instance.
    /// </summary>
    public static FileGridSearchResult Empty { get; } = new(Array.Empty<FileSummaryDto>(), 0, false);
}
