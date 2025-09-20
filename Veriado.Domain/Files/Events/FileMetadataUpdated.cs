using System;
using Veriado.Domain.Metadata;
using Veriado.Domain.Primitives;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.Files.Events;

/// <summary>
/// Domain event emitted when descriptive metadata of a file changes.
/// </summary>
public sealed class FileMetadataUpdated : IDomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileMetadataUpdated"/> class.
    /// </summary>
    /// <param name="fileId">The identifier of the file.</param>
    /// <param name="mime">The updated MIME type.</param>
    /// <param name="author">The updated author.</param>
    /// <param name="systemMetadata">The updated file system metadata snapshot.</param>
    public FileMetadataUpdated(Guid fileId, MimeType mime, string author, FileSystemMetadata systemMetadata)
    {
        FileId = fileId;
        Mime = mime;
        Author = author;
        SystemMetadata = systemMetadata;
        EventId = Guid.NewGuid();
        OccurredOnUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the identifier of the file.
    /// </summary>
    public Guid FileId { get; }

    /// <summary>
    /// Gets the MIME type after the update.
    /// </summary>
    public MimeType Mime { get; }

    /// <summary>
    /// Gets the author after the update.
    /// </summary>
    public string Author { get; }

    /// <summary>
    /// Gets the file system metadata snapshot after the update.
    /// </summary>
    public FileSystemMetadata SystemMetadata { get; }

    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; }
}
