namespace Veriado.Contracts.Import;

/// <summary>
/// Represents configurable behaviour and resource limits for the streaming import pipeline.
/// </summary>
public sealed record class ImportOptions
{
    /// <summary>
    /// Gets or sets the maximum allowed size of an imported file in bytes.
    /// A non-positive value or <c>null</c> disables the limit.
    /// </summary>
    public long? MaxFileSizeBytes { get; init; }
        = null;

    /// <summary>
    /// Gets or sets the maximum number of files processed concurrently.
    /// Values lower than one are normalized to one.
    /// </summary>
    public int MaxDegreeOfParallelism { get; init; }
        = 1;

    /// <summary>
    /// Gets or sets the maximum number of files that can be read concurrently from disk.
    /// Values lower than one are normalized to one.
    /// </summary>
    public int MaxConcurrentReads { get; init; }
        = 1;

    /// <summary>
    /// Gets or sets the buffer size used while streaming file content from disk.
    /// Values lower than four kilobytes are normalized to four kilobytes.
    /// </summary>
    public int ReadBufferSize { get; init; }
        = 64 * 1024;

    /// <summary>
    /// Gets or sets the maximum number of items persisted in a single database batch.
    /// Values lower than one are normalized to one.
    /// </summary>
    public int BatchSize { get; init; }
        = 200;

    /// <summary>
    /// Gets or sets the performance profile that influences normalization of other values.
    /// </summary>
    public PerformanceProfile PerformanceProfile { get; init; }
        = PerformanceProfile.Normal;

    /// <summary>
    /// Gets or sets a value indicating whether the source file can be deleted by other processes while it is being imported.
    /// When enabled, the importer opens files with <c>FileShare.Delete</c> in addition to read/write access.
    /// </summary>
    public bool AllowSourceFileDeletion { get; init; }
        = false;

    /// <summary>
    /// Gets or sets the maximum number of retries performed when a source file is temporarily locked.
    /// </summary>
    public int FileOpenRetryCount { get; init; }
        = 5;

    /// <summary>
    /// Gets or sets the initial back-off delay, in milliseconds, applied between retries while opening source files.
    /// </summary>
    public int FileOpenRetryBaseDelayMilliseconds { get; init; }
        = 200;

    /// <summary>
    /// Gets or sets the maximum back-off delay, in milliseconds, applied when retrying to open source files.
    /// </summary>
    public int FileOpenRetryMaxDelayMilliseconds { get; init; }
        = 2000;

    /// <summary>
    /// Gets or sets the default author applied when file metadata does not specify one.
    /// </summary>
    public string? DefaultAuthor { get; init; }
        = null;

    /// <summary>
    /// Gets or sets a value indicating whether captured file system metadata should be preserved.
    /// </summary>
    public bool KeepFileSystemMetadata { get; init; }
        = true;

    /// <summary>
    /// Gets or sets a value indicating whether imported files should be marked as read only.
    /// </summary>
    public bool SetReadOnly { get; init; }
        = false;

    /// <summary>
    /// Gets or sets the search pattern applied when enumerating files.
    /// </summary>
    public string? SearchPattern { get; init; }
        = "*";

    /// <summary>
    /// Gets or sets a value indicating whether sub-folders should be traversed.
    /// </summary>
    public bool Recursive { get; init; }
        = true;
}
