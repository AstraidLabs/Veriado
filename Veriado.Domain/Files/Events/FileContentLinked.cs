using Veriado.Domain.Files;

namespace Veriado.Domain.Files.Events;

/// <summary>
/// Domain event emitted when file content is linked to external storage.
/// </summary>
public sealed class FileContentLinked : IDomainEvent
{
    public FileContentLinked(
        Guid fileId,
        Guid fileSystemId,
        FileContentLink content,
        UtcTimestamp occurredUtc)
    {
        FileId = fileId;
        FileSystemId = fileSystemId;
        Content = content;
        EventId = Guid.NewGuid();
        OccurredOnUtc = occurredUtc.ToDateTimeOffset();
    }

    public Guid FileId { get; }

    public Guid FileSystemId { get; }

    public FileContentLink Content { get; }

    public Guid EventId { get; }

    public DateTimeOffset OccurredOnUtc { get; }
}
