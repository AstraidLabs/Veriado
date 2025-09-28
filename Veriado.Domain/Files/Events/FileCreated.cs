namespace Veriado.Domain.Files.Events;

/// <summary>
/// Domain event emitted when a new file aggregate is created.
/// </summary>
public sealed class FileCreated : IDomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileCreated"/> class.
    /// </summary>
    /// <param name="fileId">The identifier of the file.</param>
    /// <param name="name">The file name.</param>
    /// <param name="extension">The file extension.</param>
    /// <param name="mime">The MIME type.</param>
    /// <param name="author">The author.</param>
    /// <param name="size">The file size.</param>
    /// <param name="hash">The SHA-256 hash of the file content.</param>
    /// <param name="occurredUtc">The timestamp when the file was created.</param>
    public FileCreated(
        Guid fileId,
        FileName name,
        FileExtension extension,
        MimeType mime,
        string author,
        ByteSize size,
        FileHash hash,
        UtcTimestamp occurredUtc)
    {
        FileId = fileId;
        Name = name;
        Extension = extension;
        Mime = mime;
        Author = author;
        Size = size;
        Hash = hash;
        EventId = Guid.NewGuid();
        OccurredOnUtc = occurredUtc.ToDateTimeOffset();
    }

    /// <summary>
    /// Gets the identifier of the file.
    /// </summary>
    public Guid FileId { get; }

    /// <summary>
    /// Gets the file name.
    /// </summary>
    public FileName Name { get; }

    /// <summary>
    /// Gets the file extension.
    /// </summary>
    public FileExtension Extension { get; }

    /// <summary>
    /// Gets the file MIME type.
    /// </summary>
    public MimeType Mime { get; }

    /// <summary>
    /// Gets the author stored in the file metadata.
    /// </summary>
    public string Author { get; }

    /// <summary>
    /// Gets the size of the file.
    /// </summary>
    public ByteSize Size { get; }

    /// <summary>
    /// Gets the SHA-256 hash of the file content.
    /// </summary>
    public FileHash Hash { get; }

    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; }
}
