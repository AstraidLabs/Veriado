// VERIADO REFACTOR
namespace Veriado.Domain.Files.Events;

/// <summary>
/// Domain event emitted when file content is disassociated from a file.
/// </summary>
public sealed class FileContentUnlinked : IDomainEvent
{
    // VERIADO REFACTOR
    public FileContentUnlinked(Guid fileId, UtcTimestamp occurredUtc)
    {
        FileId = fileId;
        EventId = Guid.NewGuid();
        OccurredOnUtc = occurredUtc.ToDateTimeOffset();
    }

    // VERIADO REFACTOR
    public Guid FileId { get; }

    // VERIADO REFACTOR
    public Guid EventId { get; }

    // VERIADO REFACTOR
    public DateTimeOffset OccurredOnUtc { get; }
}
