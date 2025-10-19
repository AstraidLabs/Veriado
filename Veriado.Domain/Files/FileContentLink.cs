using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.Files;

/// <summary>
/// Represents an immutable snapshot describing how a file points to binary content.
/// </summary>
public sealed record FileContentLink
{
    private FileContentLink(
        string provider,
        string location,
        FileHash contentHash,
        ByteSize size,
        ContentVersion version,
        UtcTimestamp createdUtc,
        MimeType? mime)
    {
        Provider = provider;
        Location = location;
        ContentHash = contentHash;
        Size = size;
        Version = version;
        CreatedUtc = createdUtc;
        Mime = mime;
    }

    /// <summary>
    /// Gets the storage provider identifier hosting the content.
    /// </summary>
    public string Provider { get; }

    /// <summary>
    /// Gets the provider specific location pointing to the stored payload.
    /// </summary>
    public string Location { get; }

    /// <summary>
    /// Gets the hash of the linked content.
    /// </summary>
    public FileHash ContentHash { get; }

    /// <summary>
    /// Gets the size of the linked payload in bytes.
    /// </summary>
    public ByteSize Size { get; }

    /// <summary>
    /// Gets the semantic content version.
    /// </summary>
    public ContentVersion Version { get; }

    /// <summary>
    /// Gets the timestamp when the link was created.
    /// </summary>
    public UtcTimestamp CreatedUtc { get; }

    /// <summary>
    /// Gets the MIME type of the linked content if known.
    /// </summary>
    public MimeType? Mime { get; }

    /// <summary>
    /// Creates a new <see cref="FileContentLink"/> enforcing the invariants of the snapshot.
    /// </summary>
    /// <param name="provider">The storage provider identifier.</param>
    /// <param name="location">The provider specific location reference.</param>
    /// <param name="contentHash">The hash of the linked content.</param>
    /// <param name="size">The content size.</param>
    /// <param name="version">The semantic content version.</param>
    /// <param name="createdUtc">The timestamp when the link was established.</param>
    /// <param name="mime">Optional MIME type.</param>
    /// <returns>The created value object.</returns>
    public static FileContentLink Create(
        string provider,
        string location,
        FileHash contentHash,
        ByteSize size,
        ContentVersion version,
        UtcTimestamp createdUtc,
        MimeType? mime = null)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("Provider cannot be null or whitespace.", nameof(provider));
        }

        if (string.IsNullOrWhiteSpace(location))
        {
            throw new ArgumentException("Location cannot be null or whitespace.", nameof(location));
        }

        var normalizedProvider = provider.Trim();
        var normalizedLocation = location.Trim();

        return new FileContentLink(
            normalizedProvider,
            normalizedLocation,
            contentHash,
            size,
            version,
            createdUtc,
            mime);
    }
}
