namespace Veriado.Contracts.Files;

/// <summary>
/// Represents the full set of parameters accepted by the advanced file grid query.
/// </summary>
public sealed record FileGridQueryDto
{
    /// <summary>
    /// Gets or sets the full-text search text.
    /// </summary>
    public string? Text { get; init; }
        = null;

    /// <summary>
    /// Gets or sets a value indicating whether full-text terms should use prefix matching.
    /// </summary>
    public bool TextPrefix { get; init; }
        = true;

    /// <summary>
    /// Gets or sets a value indicating whether all terms must match.
    /// </summary>
    public bool TextAllTerms { get; init; }
        = true;

    /// <summary>
    /// Gets or sets the optional saved query key to reuse a stored match expression.
    /// </summary>
    public string? SavedQueryKey { get; init; }
        = null;

    /// <summary>
    /// Gets or sets a filter applied to the file name.
    /// </summary>
    public string? Name { get; init; }
        = null;

    /// <summary>
    /// Gets or sets a filter applied to the file extension without the leading dot.
    /// </summary>
    public string? Extension { get; init; }
        = null;

    /// <summary>
    /// Gets or sets a filter applied to the MIME type.
    /// </summary>
    public string? Mime { get; init; }
        = null;

    /// <summary>
    /// Gets or sets a filter applied to the file author.
    /// </summary>
    public string? Author { get; init; }
        = null;

    /// <summary>
    /// Gets or sets a filter for the read-only state of the file.
    /// </summary>
    public bool? IsReadOnly { get; init; }
        = null;

    /// <summary>
    /// Gets or sets a filter indicating whether the search index entry is stale.
    /// </summary>
    public bool? IsIndexStale { get; init; }
        = null;

    /// <summary>
    /// Gets or sets a filter requiring the file to have validity information.
    /// </summary>
    public bool? HasValidity { get; init; }
        = null;

    /// <summary>
    /// Gets or sets a filter for files that are currently valid.
    /// </summary>
    public bool? IsCurrentlyValid { get; init; }
        = null;

    /// <summary>
    /// Gets or sets a filter for files expiring within the specified number of days.
    /// </summary>
    public int? ExpiringInDays { get; init; }
        = null;

    /// <summary>
    /// Gets or sets the high-level validity filter mode.
    /// </summary>
    public ValidityFilterMode ValidityFilterMode { get; init; }
        = ValidityFilterMode.None;

    /// <summary>
    /// Gets or sets the unit used by relative validity filters.
    /// </summary>
    public ValidityRelativeUnit ValidityFilterUnit { get; init; }
        = ValidityRelativeUnit.Days;

    /// <summary>
    /// Gets or sets the single relative value (for expiring-within scenarios).
    /// </summary>
    public int? ValidityFilterValue { get; init; }
        = null;

    /// <summary>
    /// Gets or sets the lower bound of the relative validity range.
    /// </summary>
    public int? ValidityFilterRangeFrom { get; init; }
        = null;

    /// <summary>
    /// Gets or sets the upper bound of the relative validity range.
    /// </summary>
    public int? ValidityFilterRangeTo { get; init; }
        = null;

    /// <summary>
    /// Gets or sets the minimum file size in bytes.
    /// </summary>
    public long? SizeMin { get; init; }
        = null;

    /// <summary>
    /// Gets or sets the maximum file size in bytes.
    /// </summary>
    public long? SizeMax { get; init; }
        = null;

    /// <summary>
    /// Gets or sets the inclusive lower bound for the creation timestamp.
    /// </summary>
    public DateTimeOffset? CreatedFromUtc { get; init; }
        = null;

    /// <summary>
    /// Gets or sets the inclusive upper bound for the creation timestamp.
    /// </summary>
    public DateTimeOffset? CreatedToUtc { get; init; }
        = null;

    /// <summary>
    /// Gets or sets the inclusive lower bound for the last modification timestamp.
    /// </summary>
    public DateTimeOffset? ModifiedFromUtc { get; init; }
        = null;

    /// <summary>
    /// Gets or sets the inclusive upper bound for the last modification timestamp.
    /// </summary>
    public DateTimeOffset? ModifiedToUtc { get; init; }
        = null;

    /// <summary>
    /// Gets or sets a filter applied to the file version.
    /// </summary>
    public int? Version { get; init; }
        = null;

    /// <summary>
    /// Gets the sort specifications applied to the result set.
    /// </summary>
    public List<FileSortSpecDto> Sort { get; init; }
        = new();

    /// <summary>
    /// Gets or sets the requested page number (1-based).
    /// </summary>
    public int Page { get; init; }
        = 1;

    /// <summary>
    /// Gets or sets the requested page size.
    /// </summary>
    public int PageSize { get; init; }
        = 25;
}
