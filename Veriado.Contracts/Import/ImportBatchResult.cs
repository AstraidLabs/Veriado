using System.Collections.Generic;

namespace Veriado.Contracts.Import;

/// <summary>
/// Represents the aggregated outcome of a folder import operation.
/// </summary>
/// <param name="Status">The resulting status of the batch import.</param>
/// <param name="Total">The total number of processed files.</param>
/// <param name="Succeeded">The number of files imported successfully.</param>
/// <param name="Failed">The number of files that failed to import.</param>
/// <param name="Errors">The collection of import errors captured during processing.</param>
public sealed record ImportBatchResult(
    ImportBatchStatus Status,
    int Total,
    int Succeeded,
    int Failed,
    IReadOnlyList<ImportError> Errors);
