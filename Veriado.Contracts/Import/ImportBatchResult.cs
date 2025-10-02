namespace Veriado.Contracts.Import;

/// <summary>
/// Represents the aggregated outcome of a folder import operation.
/// </summary>
/// <param name="Status">The resulting status of the batch import.</param>
/// <param name="Total">The total number of files discovered for import.</param>
/// <param name="Processed">The number of files that were actually processed.</param>
/// <param name="Succeeded">The number of files imported successfully.</param>
/// <param name="Failed">The number of files that failed to import.</param>
/// <param name="Skipped">The number of files that were intentionally skipped.</param>
/// <param name="Errors">The collection of import errors captured during processing.</param>
public sealed record ImportBatchResult(
    ImportBatchStatus Status,
    int Total,
    int Processed,
    int Succeeded,
    int Failed,
    int Skipped,
    IReadOnlyList<ImportError> Errors);
