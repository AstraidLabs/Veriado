using Veriado.Domain.Metadata;

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
    /// <param name="title">The updated title, if any.</param>
    /// <param name="systemMetadata">The updated file system metadata snapshot.</param>
    /// <param name="occurredUtc">The timestamp when the update was recorded.</param>
    public FileMetadataUpdated(
        Guid fileId,
        MimeType mime,
        string author,
        string? title,
        FileSystemMetadata systemMetadata,
        UtcTimestamp occurredUtc)
    {
        FileId = fileId;
        Mime = mime;
        Author = author;
        Title = title;
        SystemMetadata = systemMetadata;
        EventId = Guid.NewGuid();
        OccurredOnUtc = occurredUtc.ToDateTimeOffset();
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
    /// Gets the optional title after the update.
    /// </summary>
    public string? Title { get; }

    /// <summary>
    /// Gets the file system metadata snapshot after the update.
    /// </summary>
    public FileSystemMetadata SystemMetadata { get; }

    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; }
}
