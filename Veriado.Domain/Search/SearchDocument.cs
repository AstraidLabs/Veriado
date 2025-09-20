using System;

namespace Veriado.Domain.Search;

/// <summary>
/// Represents the payload indexed by the full-text engine (FTS5-ready, without tags).
/// </summary>
public sealed class SearchDocument
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SearchDocument"/> class.
    /// </summary>
    public SearchDocument(
        Guid fileId,
        string title,
        string extension,
        string mimeType,
        string author,
        string? extractedText,
        DateTimeOffset createdUtc,
        DateTimeOffset lastModifiedUtc,
        string contentHash,
        long sizeInBytes,
        bool hasPhysicalCopy,
        bool hasElectronicCopy,
        DateTimeOffset? validFromUtc,
        DateTimeOffset? validUntilUtc,
        int version)
    {
        FileId = fileId;
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Extension = extension ?? throw new ArgumentNullException(nameof(extension));
        MimeType = mimeType ?? throw new ArgumentNullException(nameof(mimeType));
        Author = author ?? throw new ArgumentNullException(nameof(author));
        ExtractedText = extractedText;
        CreatedUtc = createdUtc.ToUniversalTime();
        LastModifiedUtc = lastModifiedUtc.ToUniversalTime();
        ContentHash = contentHash ?? throw new ArgumentNullException(nameof(contentHash));
        SizeInBytes = sizeInBytes;
        HasPhysicalCopy = hasPhysicalCopy;
        HasElectronicCopy = hasElectronicCopy;
        ValidFromUtc = validFromUtc?.ToUniversalTime();
        ValidUntilUtc = validUntilUtc?.ToUniversalTime();
        Version = version;
    }

    /// <summary>
    /// Gets the identifier of the file aggregate.
    /// </summary>
    public Guid FileId { get; }

    /// <summary>
    /// Gets the title of the file for search indexing (derived from name and extension).
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the file extension.
    /// </summary>
    public string Extension { get; }

    /// <summary>
    /// Gets the MIME type used for indexing.
    /// </summary>
    public string MimeType { get; }

    /// <summary>
    /// Gets the author metadata.
    /// </summary>
    public string Author { get; }

    /// <summary>
    /// Gets optional extracted text from the file content.
    /// </summary>
    public string? ExtractedText { get; }

    /// <summary>
    /// Gets the creation timestamp (UTC).
    /// </summary>
    public DateTimeOffset CreatedUtc { get; }

    /// <summary>
    /// Gets the last modification timestamp (UTC).
    /// </summary>
    public DateTimeOffset LastModifiedUtc { get; }

    /// <summary>
    /// Gets the SHA-256 hash of the indexed content.
    /// </summary>
    public string ContentHash { get; }

    /// <summary>
    /// Gets the size of the content in bytes.
    /// </summary>
    public long SizeInBytes { get; }

    /// <summary>
    /// Gets a value indicating whether a physical copy exists.
    /// </summary>
    public bool HasPhysicalCopy { get; }

    /// <summary>
    /// Gets a value indicating whether an electronic copy exists.
    /// </summary>
    public bool HasElectronicCopy { get; }

    /// <summary>
    /// Gets the validity start timestamp (UTC), if any.
    /// </summary>
    public DateTimeOffset? ValidFromUtc { get; }

    /// <summary>
    /// Gets the validity end timestamp (UTC), if any.
    /// </summary>
    public DateTimeOffset? ValidUntilUtc { get; }

    /// <summary>
    /// Gets the current file version.
    /// </summary>
    public int Version { get; }
}
