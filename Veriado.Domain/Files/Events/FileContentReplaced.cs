using System;
using Veriado.Domain.Primitives;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.Files.Events;

/// <summary>
/// Domain event emitted when file content is replaced.
/// </summary>
public sealed class FileContentReplaced : IDomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileContentReplaced"/> class.
    /// </summary>
    /// <param name="fileId">The identifier of the file.</param>
    /// <param name="hash">The new content hash.</param>
    /// <param name="size">The new content size.</param>
    /// <param name="version">The incremented file version.</param>
    public FileContentReplaced(Guid fileId, FileHash hash, ByteSize size, int version)
    {
        FileId = fileId;
        Hash = hash;
        Size = size;
        Version = version;
        EventId = Guid.NewGuid();
        OccurredOnUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the identifier of the file.
    /// </summary>
    public Guid FileId { get; }

    /// <summary>
    /// Gets the new content hash.
    /// </summary>
    public FileHash Hash { get; }

    /// <summary>
    /// Gets the new content size.
    /// </summary>
    public ByteSize Size { get; }

    /// <summary>
    /// Gets the file version after the replacement.
    /// </summary>
    public int Version { get; }

    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; }
}
