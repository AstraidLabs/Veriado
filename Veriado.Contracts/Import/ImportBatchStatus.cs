namespace Veriado.Contracts.Import;

/// <summary>
/// Represents the high level outcome of a batch import operation.
/// </summary>
public enum ImportBatchStatus
{
    /// <summary>
    /// All files were imported successfully.
    /// </summary>
    Success,

    /// <summary>
    /// At least one file was imported successfully and at least one failed.
    /// </summary>
    PartialSuccess,

    /// <summary>
    /// All files failed to import.
    /// </summary>
    Failure,

    /// <summary>
    /// A fatal error prevented the import from completing.
    /// </summary>
    FatalError,
}
