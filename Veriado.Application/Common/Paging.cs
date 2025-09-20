using System;
using System.Collections.Generic;

namespace Veriado.Application.Common;

/// <summary>
/// Represents a request for a page of results.
/// </summary>
public sealed class PageRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PageRequest"/> class.
    /// </summary>
    /// <param name="pageNumber">The 1-based page number.</param>
    /// <param name="pageSize">The size of the page.</param>
    public PageRequest(int pageNumber, int pageSize)
    {
        if (pageNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber), pageNumber, "Page number must be positive.");
        }

        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "Page size must be positive.");
        }

        PageNumber = pageNumber;
        PageSize = pageSize;
    }

    /// <summary>
    /// Gets the 1-based page number.
    /// </summary>
    public int PageNumber { get; }

    /// <summary>
    /// Gets the page size.
    /// </summary>
    public int PageSize { get; }

    /// <summary>
    /// Gets the number of records to skip.
    /// </summary>
    public int Skip => (PageNumber - 1) * PageSize;
}

/// <summary>
/// Represents a materialized page of items returned by a query.
/// </summary>
/// <typeparam name="T">The type of item in the page.</typeparam>
/// <param name="Items">The items in the page.</param>
/// <param name="PageNumber">The current page number.</param>
/// <param name="PageSize">The size of the page.</param>
/// <param name="TotalCount">The total number of items across all pages.</param>
public sealed record Page<T>(
    IReadOnlyList<T> Items,
    int PageNumber,
    int PageSize,
    int TotalCount)
{
    /// <summary>
    /// Gets the total number of pages based on the page size and total count.
    /// </summary>
    public int TotalPages => PageSize == 0
        ? 0
        : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
