namespace Veriado.Contracts.Common;

/// <summary>
/// Represents a paging request describing the desired page number and page size.
/// </summary>
public sealed record PageRequest
{
    /// <summary>
    /// Gets the one-based page number.
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// Gets the page size.
    /// </summary>
    public int PageSize { get; init; } = 50;
}

/// <summary>
/// Represents a paged result containing items and accompanying paging metadata.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed record PageResult<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PageResult{T}"/> record.
    /// </summary>
    /// <param name="items">The page items.</param>
    /// <param name="page">The current one-based page number.</param>
    /// <param name="pageSize">The requested page size.</param>
    /// <param name="totalCount">The total number of items across all pages.</param>
    public PageResult(IReadOnlyList<T> items, int page, int pageSize, int totalCount, bool hasMore = false, bool isTruncated = false)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (page <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(page), page, "Page number must be positive.");
        }

        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "Page size must be positive.");
        }

        if (totalCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCount), totalCount, "Total count cannot be negative.");
        }

        Items = items;
        Page = page;
        PageSize = pageSize;
        TotalCount = totalCount;
        HasMore = hasMore;
        IsTruncated = isTruncated;
    }

    /// <summary>
    /// Gets the items in the current page.
    /// </summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>
    /// Gets the one-based page number.
    /// </summary>
    public int Page { get; }

    /// <summary>
    /// Gets the page size.
    /// </summary>
    public int PageSize { get; }

    /// <summary>
    /// Gets the total number of items available.
    /// </summary>
    public int TotalCount { get; }

    /// <summary>
    /// Gets a value indicating whether another page is available after the current one.
    /// </summary>
    public bool HasMore { get; }

    /// <summary>
    /// Gets a value indicating whether the result set was truncated by policy.
    /// </summary>
    public bool IsTruncated { get; }

    /// <summary>
    /// Gets the total number of pages available.
    /// </summary>
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>
/// Backwards compatible representation of <see cref="PageRequest"/>.
/// </summary>
[Obsolete("Use PageRequest instead.")]
public sealed record PagingRequest
{
    /// <summary>
    /// Gets the one-based page number.
    /// </summary>
    public int PageNumber { get; init; } = 1;

    /// <summary>
    /// Gets the page size.
    /// </summary>
    public int PageSize { get; init; } = 50;

    /// <summary>
    /// Converts to the new <see cref="PageRequest"/> type.
    /// </summary>
    public PageRequest ToPageRequest() => new()
    {
        Page = PageNumber,
        PageSize = PageSize,
    };
}

/// <summary>
/// Backwards compatible representation of <see cref="PageResult{T}"/>.
/// </summary>
[Obsolete("Use PageResult<T> instead.")]
public sealed record PagedResult<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PagedResult{T}"/> record.
    /// </summary>
    /// <param name="items">The page items.</param>
    /// <param name="pageNumber">The current one-based page number.</param>
    /// <param name="pageSize">The requested page size.</param>
    /// <param name="totalCount">The total number of items across all pages.</param>
    public PagedResult(IReadOnlyList<T> items, int pageNumber, int pageSize, int totalCount)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (pageNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber), pageNumber, "Page number must be positive.");
        }

        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "Page size must be positive.");
        }

        if (totalCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCount), totalCount, "Total count cannot be negative.");
        }

        Items = items;
        PageNumber = pageNumber;
        PageSize = pageSize;
        TotalCount = totalCount;
    }

    /// <summary>
    /// Gets the items in the current page.
    /// </summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>
    /// Gets the one-based page number.
    /// </summary>
    public int PageNumber { get; }

    /// <summary>
    /// Gets the page size.
    /// </summary>
    public int PageSize { get; }

    /// <summary>
    /// Gets the total number of items available.
    /// </summary>
    public int TotalCount { get; }

    /// <summary>
    /// Gets the total number of pages available.
    /// </summary>
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary>
    /// Converts to the new <see cref="PageResult{T}"/> type.
    /// </summary>
    public PageResult<T> ToPageResult() => new(Items, PageNumber, PageSize, TotalCount);
}
