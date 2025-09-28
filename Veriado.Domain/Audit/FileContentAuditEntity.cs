namespace Veriado.Domain.Audit;

/// <summary>
/// Represents an audit entry describing file content replacements.
/// </summary>
public sealed class FileContentAuditEntity
{
    private FileContentAuditEntity(Guid fileId, FileHash newHash, UtcTimestamp occurredUtc)
    {
        FileId = fileId;
        NewHash = newHash;
        OccurredUtc = occurredUtc;
    }

    /// <summary>
    /// Gets the identifier of the file whose content changed.
    /// </summary>
    public Guid FileId { get; }

    /// <summary>
    /// Gets the new content hash.
    /// </summary>
    public FileHash NewHash { get; }

    /// <summary>
    /// Gets the timestamp when the audit entry was recorded.
    /// </summary>
    public UtcTimestamp OccurredUtc { get; }

    /// <summary>
    /// Creates an audit entry for a content replacement.
    /// </summary>
    /// <param name="fileId">The file identifier.</param>
    /// <param name="newHash">The new content hash.</param>
    /// <param name="occurredUtc">The timestamp when the event occurred.</param>
    /// <returns>The created audit entry.</returns>
    public static FileContentAuditEntity Replaced(Guid fileId, FileHash newHash, UtcTimestamp occurredUtc)
    {
        return new FileContentAuditEntity(fileId, newHash, occurredUtc);
    }
}
