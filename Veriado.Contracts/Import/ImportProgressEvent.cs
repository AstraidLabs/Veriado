namespace Veriado.Contracts.Import;

/// <summary>
/// Represents a strongly typed progress notification emitted by <see cref="Services.Import.IImportService"/>.
/// </summary>
/// <param name="Kind">The kind of progress signal.</param>
/// <param name="Timestamp">The UTC timestamp when the event was created.</param>
/// <param name="FilePath">The file path related to the event, if any.</param>
/// <param name="Message">An optional human readable message.</param>
/// <param name="Error">Optional error metadata when <see cref="ImportProgressKind.Error"/> is raised.</param>
/// <param name="Aggregate">Optional aggregated batch information when <see cref="ImportProgressKind.BatchCompleted"/> is raised.</param>
/// <param name="ProcessedFiles">The number of files processed so far, when available.</param>
/// <param name="TotalFiles">The total number of files in the batch, when available.</param>
/// <param name="SucceededFiles">The number of successfully imported files, when available.</param>
/// <param name="FailedFiles">The number of failed files, when available.</param>
/// <param name="ProgressPercent">The computed progress percentage in range 0-100, when available.</param>
public sealed record class ImportProgressEvent(
    ImportProgressKind Kind,
    DateTimeOffset Timestamp,
    string? FilePath,
    string? Message,
    ImportError? Error,
    ImportAggregateResult? Aggregate,
    int? ProcessedFiles,
    int? TotalFiles,
    int? SucceededFiles,
    int? FailedFiles,
    double? ProgressPercent)
{
    /// <summary>
    /// Creates a batch started event.
    /// </summary>
    public static ImportProgressEvent BatchStarted(int? totalFiles, DateTimeOffset? timestamp = null)
        => new(
            ImportProgressKind.BatchStarted,
            timestamp ?? DateTimeOffset.UtcNow,
            FilePath: null,
            Message: totalFiles is null
                ? "Import batch started."
                : $"Import batch started ({totalFiles} files).",
            Error: null,
            Aggregate: null,
            ProcessedFiles: 0,
            TotalFiles: totalFiles,
            SucceededFiles: 0,
            FailedFiles: 0,
            ProgressPercent: ComputePercent(0, totalFiles));

    /// <summary>
    /// Creates a file started event.
    /// </summary>
    public static ImportProgressEvent FileStarted(string filePath, int processed, int? total, DateTimeOffset? timestamp = null)
        => new(
            ImportProgressKind.FileStarted,
            timestamp ?? DateTimeOffset.UtcNow,
            FilePath: filePath,
            Message: $"Processing '{filePath}'.",
            Error: null,
            Aggregate: null,
            ProcessedFiles: processed,
            TotalFiles: total,
            SucceededFiles: null,
            FailedFiles: null,
            ProgressPercent: ComputePercent(processed, total));

    /// <summary>
    /// Creates a progress snapshot event.
    /// </summary>
    public static ImportProgressEvent Progress(
        int processed,
        int? total,
        int succeeded,
        int failed,
        string? message = null,
        DateTimeOffset? timestamp = null)
        => new(
            ImportProgressKind.Progress,
            timestamp ?? DateTimeOffset.UtcNow,
            FilePath: null,
            Message: message,
            Error: null,
            Aggregate: null,
            ProcessedFiles: processed,
            TotalFiles: total,
            SucceededFiles: succeeded,
            FailedFiles: failed,
            ProgressPercent: ComputePercent(processed, total));

    /// <summary>
    /// Creates a file completed event.
    /// </summary>
    public static ImportProgressEvent FileCompleted(
        string filePath,
        int processed,
        int? total,
        int succeeded,
        string? message = null,
        DateTimeOffset? timestamp = null)
        => new(
            ImportProgressKind.FileCompleted,
            timestamp ?? DateTimeOffset.UtcNow,
            FilePath: filePath,
            Message: message ?? $"Completed '{filePath}'.",
            Error: null,
            Aggregate: null,
            ProcessedFiles: processed,
            TotalFiles: total,
            SucceededFiles: succeeded,
            FailedFiles: null,
            ProgressPercent: ComputePercent(processed, total));

    /// <summary>
    /// Creates an error event.
    /// </summary>
    public static ImportProgressEvent ErrorOccurred(
        ImportError error,
        int processed,
        int? total,
        int succeeded,
        int failed,
        DateTimeOffset? timestamp = null)
        => new(
            ImportProgressKind.Error,
            timestamp ?? error.Timestamp,
            FilePath: error.FilePath,
            Message: error.Message,
            Error: error,
            Aggregate: null,
            ProcessedFiles: processed,
            TotalFiles: total,
            SucceededFiles: succeeded,
            FailedFiles: failed,
            ProgressPercent: ComputePercent(processed, total));

    /// <summary>
    /// Creates a batch completed event.
    /// </summary>
    public static ImportProgressEvent BatchCompleted(
        ImportAggregateResult aggregate,
        DateTimeOffset? timestamp = null)
        => new(
            ImportProgressKind.BatchCompleted,
            timestamp ?? DateTimeOffset.UtcNow,
            FilePath: null,
            Message: BuildCompletionMessage(aggregate),
            Error: null,
            Aggregate: aggregate,
            ProcessedFiles: aggregate.Total,
            TotalFiles: aggregate.Total,
            SucceededFiles: aggregate.Succeeded,
            FailedFiles: aggregate.Failed,
            ProgressPercent: ComputePercent(aggregate.Total, aggregate.Total));

    private static double? ComputePercent(int? processed, int? total)
    {
        if (!processed.HasValue || !total.HasValue || total.Value <= 0)
        {
            return null;
        }

        var percent = (double)processed.Value / total.Value * 100d;
        if (double.IsNaN(percent) || double.IsInfinity(percent))
        {
            return null;
        }

        return Math.Clamp(percent, 0d, 100d);
    }

    private static string BuildCompletionMessage(ImportAggregateResult aggregate)
    {
        var statusText = aggregate.Status switch
        {
            ImportBatchStatus.Success => "Import completed successfully.",
            ImportBatchStatus.PartialSuccess => "Import completed with partial success.",
            ImportBatchStatus.Failure => "Import completed with failures.",
            ImportBatchStatus.FatalError => "Import stopped due to a fatal error.",
            _ => "Import completed.",
        };

        return $"{statusText} ({aggregate.Succeeded}/{aggregate.Total} ok, {aggregate.Failed} failed).";
    }
}
