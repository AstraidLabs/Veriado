namespace Veriado.Application.Files.Configuration;

/// <summary>
/// Provides configuration for the advanced file grid query handler.
/// </summary>
public sealed class FileGridQueryOptions
{
    /// <summary>
    /// Gets or sets the maximum allowed page size.
    /// </summary>
    public int MaxPageSize { get; set; } = 200;

    /// <summary>
    /// Gets or sets the maximum number of FTS candidates evaluated before filtering.
    /// </summary>
    public int MaxCandidateResults { get; set; } = 2_000;
}
