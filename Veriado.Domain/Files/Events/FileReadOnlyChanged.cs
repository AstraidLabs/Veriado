namespace Veriado.Domain.Files.Events;

/// <summary>
/// Domain event emitted when a file's read-only flag changes.
/// </summary>
public sealed class FileReadOnlyChanged : IDomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileReadOnlyChanged"/> class.
    /// </summary>
    /// <param name="fileId">The identifier of the file.</param>
    /// <param name="isReadOnly">Indicates the current read-only state.</param>
    /// <param name="occurredUtc">The timestamp when the change occurred.</param>
    public FileReadOnlyChanged(Guid fileId, bool isReadOnly, UtcTimestamp occurredUtc)
    {
        FileId = fileId;
        IsReadOnly = isReadOnly;
        EventId = Guid.NewGuid();
        OccurredOnUtc = occurredUtc.ToDateTimeOffset();
    }

    /// <summary>
    /// Gets the identifier of the file.
    /// </summary>
    public Guid FileId { get; }

    /// <summary>
    /// Gets a value indicating whether the file is now read-only.
    /// </summary>
    public bool IsReadOnly { get; }

    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; }
}
