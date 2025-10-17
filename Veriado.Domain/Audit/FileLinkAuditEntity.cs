namespace Veriado.Domain.Audit;

/// <summary>
/// Represents an audit entry describing file content linkage to file system records.
/// </summary>
public sealed class FileLinkAuditEntity
{
    private FileLinkAuditEntity(
        Guid fileId,
        Guid fileSystemId,
        FileLinkAuditAction action,
        int version,
        string hash,
        long size,
        string mime,
        UtcTimestamp occurredUtc)
    {
        FileId = fileId;
        FileSystemId = fileSystemId;
        Action = action;
        Version = version;
        Hash = hash;
        Size = size;
        Mime = mime;
        OccurredUtc = occurredUtc;
    }

    /// <summary>
    /// Gets the identifier of the associated file.
    /// </summary>
    public Guid FileId { get; }

    /// <summary>
    /// Gets the identifier of the linked file system resource.
    /// </summary>
    public Guid FileSystemId { get; }

    /// <summary>
    /// Gets the recorded action.
    /// </summary>
    public FileLinkAuditAction Action { get; }

    /// <summary>
    /// Gets the linked content version.
    /// </summary>
    public int Version { get; }

    /// <summary>
    /// Gets the content hash that was linked.
    /// </summary>
    public string Hash { get; }

    /// <summary>
    /// Gets the size of the linked content.
    /// </summary>
    public long Size { get; }

    /// <summary>
    /// Gets the MIME type of the linked content.
    /// </summary>
    public string Mime { get; }

    /// <summary>
    /// Gets the timestamp when the audit entry was recorded.
    /// </summary>
    public UtcTimestamp OccurredUtc { get; }

    /// <summary>
    /// Creates an audit entry representing new content linkage.
    /// </summary>
    public static FileLinkAuditEntity Linked(
        Guid fileId,
        Guid fileSystemId,
        ContentVersion version,
        FileHash hash,
        ByteSize size,
        MimeType mime,
        UtcTimestamp occurredUtc)
    {
        return new FileLinkAuditEntity(
            fileId,
            fileSystemId,
            FileLinkAuditAction.Linked,
            version.Value,
            hash.Value,
            size.Value,
            mime.Value,
            occurredUtc);
    }

    /// <summary>
    /// Creates an audit entry representing content re-linkage.
    /// </summary>
    public static FileLinkAuditEntity Relinked(
        Guid fileId,
        Guid fileSystemId,
        ContentVersion version,
        FileHash hash,
        ByteSize size,
        MimeType mime,
        UtcTimestamp occurredUtc)
    {
        return new FileLinkAuditEntity(
            fileId,
            fileSystemId,
            FileLinkAuditAction.Relinked,
            version.Value,
            hash.Value,
            size.Value,
            mime.Value,
            occurredUtc);
    }
}

/// <summary>
/// Enumerates supported audit actions for file content linkage events.
/// </summary>
public enum FileLinkAuditAction
{
    /// <summary>
    /// Indicates that file content was linked for the first time.
    /// </summary>
    Linked,

    /// <summary>
    /// Indicates that file content was re-linked to new storage metadata.
    /// </summary>
    Relinked,
}
