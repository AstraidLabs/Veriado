using System;
using System.Collections.Generic;

namespace Veriado.Contracts.Import;

/// <summary>
/// Represents the aggregated outcome of a streaming import batch.
/// </summary>
/// <param name="Status">The resulting status of the batch.</param>
/// <param name="Total">The total number of processed files.</param>
/// <param name="Succeeded">The number of successfully imported files.</param>
/// <param name="Failed">The number of files that failed to import.</param>
/// <param name="Errors">The collection of captured errors.</param>
public sealed record class ImportAggregateResult(
    ImportBatchStatus Status,
    int Total,
    int Succeeded,
    int Failed,
    IReadOnlyList<ImportError> Errors)
{
    /// <summary>
    /// Gets an empty success result.
    /// </summary>
    public static ImportAggregateResult EmptySuccess { get; }
        = new(ImportBatchStatus.Success, 0, 0, 0, Array.Empty<ImportError>());
}
