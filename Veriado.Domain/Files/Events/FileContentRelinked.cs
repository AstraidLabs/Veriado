// VERIADO REFACTOR
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.Files.Events;

/// <summary>
/// Domain event emitted when file content is re-linked to new storage metadata.
/// </summary>
public sealed class FileContentRelinked : IDomainEvent
{
    // VERIADO REFACTOR
    public FileContentRelinked(
        Guid fileId,
        Guid fileSystemId,
        StorageProvider provider,
        StoragePath path,
        FileHash hash,
        ContentVersion version,
        UtcTimestamp occurredUtc)
    {
        FileId = fileId;
        FileSystemId = fileSystemId;
        Provider = provider;
        Path = path;
        Hash = hash;
        Version = version;
        EventId = Guid.NewGuid();
        OccurredOnUtc = occurredUtc.ToDateTimeOffset();
    }

    // VERIADO REFACTOR
    public Guid FileId { get; }

    // VERIADO REFACTOR
    public Guid FileSystemId { get; }

    // VERIADO REFACTOR
    public StorageProvider Provider { get; }

    // VERIADO REFACTOR
    public StoragePath Path { get; }

    // VERIADO REFACTOR
    public FileHash Hash { get; }

    // VERIADO REFACTOR
    public ContentVersion Version { get; }

    // VERIADO REFACTOR
    public Guid EventId { get; }

    // VERIADO REFACTOR
    public DateTimeOffset OccurredOnUtc { get; }
}
