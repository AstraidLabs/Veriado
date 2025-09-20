using System;

namespace Veriado.Domain.Audit;

/// <summary>
/// Represents an auditable snapshot of validity information changes.
/// </summary>
public sealed class FileDocumentValidityAuditEntity
{
    private FileDocumentValidityAuditEntity(
        Guid fileId,
        string action,
        DateTimeOffset occurredAtUtc,
        string? actor,
        DateTimeOffset? issuedAtUtc,
        DateTimeOffset? validUntilUtc,
        bool hasPhysicalCopy,
        bool hasElectronicCopy)
    {
        FileId = fileId;
        Action = action ?? throw new ArgumentNullException(nameof(action));
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
        Actor = actor;
        IssuedAtUtc = issuedAtUtc?.ToUniversalTime();
        ValidUntilUtc = validUntilUtc?.ToUniversalTime();
        HasPhysicalCopy = hasPhysicalCopy;
        HasElectronicCopy = hasElectronicCopy;
    }

    /// <summary>
    /// Gets the affected file identifier.
    /// </summary>
    public Guid FileId { get; }

    /// <summary>
    /// Gets the action label.
    /// </summary>
    public string Action { get; }

    /// <summary>
    /// Gets the UTC timestamp of the audit entry.
    /// </summary>
    public DateTimeOffset OccurredAtUtc { get; }

    /// <summary>
    /// Gets optional actor information.
    /// </summary>
    public string? Actor { get; }

    /// <summary>
    /// Gets the issuance timestamp, if any.
    /// </summary>
    public DateTimeOffset? IssuedAtUtc { get; }

    /// <summary>
    /// Gets the expiration timestamp, if any.
    /// </summary>
    public DateTimeOffset? ValidUntilUtc { get; }

    /// <summary>
    /// Gets a value indicating whether a physical copy exists.
    /// </summary>
    public bool HasPhysicalCopy { get; }

    /// <summary>
    /// Gets a value indicating whether an electronic copy exists.
    /// </summary>
    public bool HasElectronicCopy { get; }

    /// <summary>
    /// Records addition of validity information.
    /// </summary>
    public static FileDocumentValidityAuditEntity Added(Guid fileId, DateTimeOffset issuedAtUtc, DateTimeOffset validUntilUtc, bool hasPhysicalCopy, bool hasElectronicCopy, DateTimeOffset occurredAtUtc, string? actor = null)
        => new(fileId, "Added", occurredAtUtc, actor, issuedAtUtc, validUntilUtc, hasPhysicalCopy, hasElectronicCopy);

    /// <summary>
    /// Records an update to existing validity information.
    /// </summary>
    public static FileDocumentValidityAuditEntity Updated(Guid fileId, DateTimeOffset issuedAtUtc, DateTimeOffset validUntilUtc, bool hasPhysicalCopy, bool hasElectronicCopy, DateTimeOffset occurredAtUtc, string? actor = null)
        => new(fileId, "Updated", occurredAtUtc, actor, issuedAtUtc, validUntilUtc, hasPhysicalCopy, hasElectronicCopy);

    /// <summary>
    /// Records removal of validity information.
    /// </summary>
    public static FileDocumentValidityAuditEntity Removed(Guid fileId, DateTimeOffset occurredAtUtc, string? actor = null)
        => new(fileId, "Removed", occurredAtUtc, actor, null, null, false, false);
}
