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
    /// Gets or sets a value indicating whether binary content extraction should be performed.
    /// </summary>
    public bool ExtractContent { get; init; }
        = true;

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
}
