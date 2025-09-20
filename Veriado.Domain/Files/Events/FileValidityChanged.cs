using System;
using Veriado.Domain.Primitives;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.Files.Events;

/// <summary>
/// Domain event emitted when the document validity information changes.
/// </summary>
public sealed class FileValidityChanged : IDomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileValidityChanged"/> class.
    /// </summary>
    /// <param name="fileId">The identifier of the file.</param>
    /// <param name="issuedAt">The validity issue timestamp, if any.</param>
    /// <param name="validUntil">The validity expiration timestamp, if any.</param>
    /// <param name="hasPhysicalCopy">Whether a physical copy exists.</param>
    /// <param name="hasElectronicCopy">Whether an electronic copy exists.</param>
    public FileValidityChanged(Guid fileId, UtcTimestamp? issuedAt, UtcTimestamp? validUntil, bool hasPhysicalCopy, bool hasElectronicCopy)
    {
        FileId = fileId;
        IssuedAt = issuedAt;
        ValidUntil = validUntil;
        HasPhysicalCopy = hasPhysicalCopy;
        HasElectronicCopy = hasElectronicCopy;
        EventId = Guid.NewGuid();
        OccurredOnUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the identifier of the file.
    /// </summary>
    public Guid FileId { get; }

    /// <summary>
    /// Gets the validity issue timestamp, if present.
    /// </summary>
    public UtcTimestamp? IssuedAt { get; }

    /// <summary>
    /// Gets the validity expiration timestamp, if present.
    /// </summary>
    public UtcTimestamp? ValidUntil { get; }

    /// <summary>
    /// Gets a value indicating whether a physical copy exists.
    /// </summary>
    public bool HasPhysicalCopy { get; }

    /// <summary>
    /// Gets a value indicating whether an electronic copy exists.
    /// </summary>
    public bool HasElectronicCopy { get; }

    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; }
}
