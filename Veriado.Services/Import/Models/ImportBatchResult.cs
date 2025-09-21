using System.Collections.Generic;

namespace Veriado.Services.Import.Models;

/// <summary>
/// Represents the aggregated outcome of a folder import operation.
/// </summary>
public sealed class ImportBatchResult
{
    public ImportBatchResult(int total, int succeeded, int failed, IReadOnlyList<ImportError> errors)
    {
        Total = total;
        Succeeded = succeeded;
        Failed = failed;
        Errors = errors;
    }

    /// <summary>
    /// Gets the total number of files processed.
    /// </summary>
    public int Total { get; }

    /// <summary>
    /// Gets the number of files imported successfully.
    /// </summary>
    public int Succeeded { get; }

    /// <summary>
    /// Gets the number of files that failed to import.
    /// </summary>
    public int Failed { get; }

    /// <summary>
    /// Gets the collection of errors captured during the import.
    /// </summary>
    public IReadOnlyList<ImportError> Errors { get; }
}
