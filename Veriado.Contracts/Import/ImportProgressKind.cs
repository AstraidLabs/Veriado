namespace Veriado.Contracts.Import;

/// <summary>
/// Describes the type of progress notification emitted during an import batch.
/// </summary>
public enum ImportProgressKind
{
    /// <summary>
    /// Signals that a batch import has started and optionally provides the expected total file count.
    /// </summary>
    BatchStarted,

    /// <summary>
    /// Signals that processing of an individual file has started.
    /// </summary>
    FileStarted,

    /// <summary>
    /// Provides an updated aggregate progress snapshot.
    /// </summary>
    Progress,

    /// <summary>
    /// Signals that an individual file has completed successfully.
    /// </summary>
    FileCompleted,

    /// <summary>
    /// Signals that an error occurred while processing the current file.
    /// </summary>
    Error,

    /// <summary>
    /// Signals that the overall batch has completed.
    /// </summary>
    BatchCompleted,
}
