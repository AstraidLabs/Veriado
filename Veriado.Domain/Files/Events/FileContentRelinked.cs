using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.Files.Events;

/// <summary>
/// Domain event emitted when file content is re-linked to new storage metadata.
/// </summary>
public sealed class FileContentRelinked : IDomainEvent
{
    public FileContentRelinked(
        Guid fileId,
        Guid fileSystemId,
        FileHash hash,
        ByteSize size,
        ContentVersion version,
        MimeType mime,
        UtcTimestamp occurredUtc)
    {
        FileId = fileId;
        FileSystemId = fileSystemId;
        Hash = hash;
        Size = size;
        Version = version;
        Mime = mime;
        EventId = Guid.NewGuid();
        OccurredOnUtc = occurredUtc.ToDateTimeOffset();
    }

    public Guid FileId { get; }

    public Guid FileSystemId { get; }

    public FileHash Hash { get; }

    public ByteSize Size { get; }

    public ContentVersion Version { get; }

    public MimeType Mime { get; }

    public Guid EventId { get; }

    public DateTimeOffset OccurredOnUtc { get; }
}
