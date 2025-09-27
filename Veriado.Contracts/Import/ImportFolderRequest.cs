namespace Veriado.Contracts.Import;

/// <summary>
/// Describes the options applied when importing all files from a folder.
/// </summary>
public sealed record class ImportFolderRequest
{
    /// <summary>
    /// Gets or sets the absolute folder path that should be scanned for files.
    /// </summary>
    public string FolderPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the default author applied when file metadata does not specify one.
    /// </summary>
    public string? DefaultAuthor { get; init; }
        = null;

    /// <summary>
    /// Gets or sets a value indicating whether captured file system metadata should be preserved.
    /// </summary>
    public bool KeepFsMetadata { get; init; }
        = true;

    /// <summary>
    /// Gets or sets a value indicating whether imported files should be marked as read only.
    /// </summary>
    public bool SetReadOnly { get; init; }
        = false;

    /// <summary>
    /// Gets or sets the maximum number of concurrent file imports.
    /// </summary>
    public int MaxDegreeOfParallelism { get; init; }
        = 4;

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

    /// <summary>
    /// Gets or sets the optional maximum allowed size of a single imported file in bytes.
    /// A non-positive value or <c>null</c> means no limit is applied.
    /// </summary>
    public long? MaxFileSizeBytes { get; init; }
        = null;
}
