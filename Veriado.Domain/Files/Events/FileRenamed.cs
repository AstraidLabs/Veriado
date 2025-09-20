using System;
using Veriado.Domain.Primitives;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.Files.Events;

/// <summary>
/// Domain event emitted when a file is renamed.
/// </summary>
public sealed class FileRenamed : IDomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileRenamed"/> class.
    /// </summary>
    /// <param name="fileId">The identifier of the file.</param>
    /// <param name="oldName">The old file name.</param>
    /// <param name="newName">The new file name.</param>
    public FileRenamed(Guid fileId, FileName oldName, FileName newName)
    {
        FileId = fileId;
        OldName = oldName;
        NewName = newName;
        EventId = Guid.NewGuid();
        OccurredOnUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the identifier of the file.
    /// </summary>
    public Guid FileId { get; }

    /// <summary>
    /// Gets the old file name.
    /// </summary>
    public FileName OldName { get; }

    /// <summary>
    /// Gets the new file name.
    /// </summary>
    public FileName NewName { get; }

    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; }
}
