namespace Veriado.Contracts.Files;

/// <summary>
/// Represents the physical location and metadata of stored file content.
/// </summary>
public sealed record FileContentLocationDto
{
    /// <summary>
    /// Gets the identifier of the logical file.
    /// </summary>
    public Guid FileId { get; init; }

    /// <summary>
    /// Gets the absolute path to the stored content.
    /// </summary>
    public string FullPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the MIME type associated with the stored content, if known.
    /// </summary>
    public string? Mime { get; init; }

    /// <summary>
    /// Gets the size of the stored content in bytes.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// Gets the creation timestamp of the stored content in UTC, if available.
    /// </summary>
    public DateTimeOffset? CreatedUtc { get; init; }

    /// <summary>
    /// Gets the last write timestamp of the stored content in UTC, if available.
    /// </summary>
    public DateTimeOffset? LastWriteUtc { get; init; }

    /// <summary>
    /// Gets the last access timestamp of the stored content in UTC, if available.
    /// </summary>
    public DateTimeOffset? LastAccessUtc { get; init; }

    /// <summary>
    /// Gets the hash of the stored content, if known.
    /// </summary>
    public string? Hash { get; init; }

    /// <summary>
    /// Gets the storage provider hosting the content.
    /// </summary>
    public string? StorageProvider { get; init; }

    /// <summary>
    /// Gets the current physical health state of the content, if tracked.
    /// </summary>
    public string? PhysicalState { get; init; }

    /// <summary>
    /// Gets a value indicating whether the content is encrypted at rest.
    /// </summary>
    public bool IsEncrypted { get; init; }
}
