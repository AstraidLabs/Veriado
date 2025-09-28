namespace Veriado.Contracts.Import;

/// <summary>
/// Represents configurable import behavior and resource limits.
/// </summary>
public sealed record class ImportOptions
{
    /// <summary>
    /// Gets or sets the search pattern applied when enumerating files within the folder.
    /// </summary>
    public string SearchPattern { get; init; } = "*";

    /// <summary>
    /// Gets or sets a value indicating whether nested folders should be processed.
    /// </summary>
    public bool Recursive { get; init; } = true;

    /// <summary>
    /// Gets or sets the default author applied when the source file does not specify one.
    /// </summary>
    public string? DefaultAuthor { get; init; }
        = null;

    /// <summary>
    /// Gets or sets a value indicating whether captured file system metadata should be preserved.
    /// </summary>
    public bool KeepFileSystemMetadata { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the imported file should be marked read-only.
    /// </summary>
    public bool SetReadOnly { get; init; }
        = false;

    /// <summary>
    /// Gets or sets the optional maximum allowed size of an imported file in bytes.
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
    /// Gets or sets the buffer size used while streaming file content from disk.
    /// Values lower than four kilobytes are normalized to four kilobytes.
    /// </summary>
    public int BufferSize { get; init; }
        = 64 * 1024;
}
