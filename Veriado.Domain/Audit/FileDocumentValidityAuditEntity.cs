namespace Veriado.Domain.Audit;

/// <summary>
/// Represents an audit entry describing changes to document validity.
/// </summary>
public sealed class FileDocumentValidityAuditEntity
{
    private FileDocumentValidityAuditEntity(Guid fileId, UtcTimestamp? issuedAt, UtcTimestamp? validUntil, bool hasPhysicalCopy, bool hasElectronicCopy, UtcTimestamp occurredUtc)
    {
        FileId = fileId;
        IssuedAt = issuedAt;
        ValidUntil = validUntil;
        HasPhysicalCopy = hasPhysicalCopy;
        HasElectronicCopy = hasElectronicCopy;
        OccurredUtc = occurredUtc;
    }

    /// <summary>
    /// Gets the identifier of the file whose validity changed.
    /// </summary>
    public Guid FileId { get; }

    /// <summary>
    /// Gets the issue timestamp after the change, if any.
    /// </summary>
    public UtcTimestamp? IssuedAt { get; }

    /// <summary>
    /// Gets the expiration timestamp after the change, if any.
    /// </summary>
    public UtcTimestamp? ValidUntil { get; }

    /// <summary>
    /// Gets a value indicating whether a physical copy exists after the change.
    /// </summary>
    public bool HasPhysicalCopy { get; }

    /// <summary>
    /// Gets a value indicating whether an electronic copy exists after the change.
    /// </summary>
    public bool HasElectronicCopy { get; }

    /// <summary>
    /// Gets the timestamp when the audit entry was recorded.
    /// </summary>
    public UtcTimestamp OccurredUtc { get; }

    /// <summary>
    /// Creates an audit entry for a validity change.
    /// </summary>
    /// <param name="fileId">The file identifier.</param>
    /// <param name="issuedAt">The new issue timestamp.</param>
    /// <param name="validUntil">The new expiration timestamp.</param>
    /// <param name="hasPhysicalCopy">Whether a physical copy exists after the change.</param>
    /// <param name="hasElectronicCopy">Whether an electronic copy exists after the change.</param>
    /// <param name="occurredUtc">The timestamp when the event occurred.</param>
    /// <returns>The created audit entry.</returns>
    public static FileDocumentValidityAuditEntity Changed(
        Guid fileId,
        UtcTimestamp? issuedAt,
        UtcTimestamp? validUntil,
        bool hasPhysicalCopy,
        bool hasElectronicCopy,
        UtcTimestamp occurredUtc)
    {
        if (issuedAt.HasValue && validUntil.HasValue && validUntil.Value.Value < issuedAt.Value.Value)
        {
            throw new ArgumentException("Valid-until must be greater than or equal to issued-at.", nameof(validUntil));
        }

        return new FileDocumentValidityAuditEntity(fileId, issuedAt, validUntil, hasPhysicalCopy, hasElectronicCopy, occurredUtc);
    }
}
