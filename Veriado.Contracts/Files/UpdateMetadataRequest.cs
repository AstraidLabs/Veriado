namespace Veriado.Contracts.Files;

/// <summary>
/// Represents the payload required to update metadata for an existing file.
/// </summary>
public sealed class UpdateMetadataRequest
{
    /// <summary>
    /// Gets or sets the identifier of the file to update.
    /// </summary>
    public Guid FileId { get; init; }

    /// <summary>
    /// Gets or sets the optional new MIME type.
    /// </summary>
    public string? Mime { get; init; }

    /// <summary>
    /// Gets or sets the optional new author.
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    /// Gets or sets an optional flag indicating whether the file should be read-only.
    /// </summary>
    public bool? IsReadOnly { get; init; }

    /// <summary>
    /// Gets or sets an optional system metadata snapshot to apply.
    /// </summary>
    public FileSystemMetadataDto? SystemMetadata { get; init; }

}
