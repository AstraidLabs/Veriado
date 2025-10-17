namespace Veriado.Domain.Audit;

/// <summary>
/// Represents an audit entry capturing file system level events.
/// </summary>
public sealed class FileSystemAuditEntity
{
    private FileSystemAuditEntity(
        Guid fileSystemId,
        FileSystemAuditAction action,
        string? path,
        string? hash,
        long? size,
        string? mime,
        int? attributes,
        string? ownerSid,
        bool? isEncrypted,
        UtcTimestamp occurredUtc)
    {
        FileSystemId = fileSystemId;
        Action = action;
        Path = path;
        Hash = hash;
        Size = size;
        Mime = mime;
        Attributes = attributes;
        OwnerSid = ownerSid;
        IsEncrypted = isEncrypted;
        OccurredUtc = occurredUtc;
    }

    /// <summary>
    /// Gets the identifier of the audited file system resource.
    /// </summary>
    public Guid FileSystemId { get; }

    /// <summary>
    /// Gets the audit action.
    /// </summary>
    public FileSystemAuditAction Action { get; }

    /// <summary>
    /// Gets the storage path associated with the event, if any.
    /// </summary>
    public string? Path { get; }

    /// <summary>
    /// Gets the hash associated with the event, if any.
    /// </summary>
    public string? Hash { get; }

    /// <summary>
    /// Gets the size associated with the event, if any.
    /// </summary>
    public long? Size { get; }

    /// <summary>
    /// Gets the MIME type associated with the event, if any.
    /// </summary>
    public string? Mime { get; }

    /// <summary>
    /// Gets the file attributes associated with the event, if any.
    /// </summary>
    public int? Attributes { get; }

    /// <summary>
    /// Gets the owner SID associated with the event, if any.
    /// </summary>
    public string? OwnerSid { get; }

    /// <summary>
    /// Gets a value indicating whether the content is encrypted, if known.
    /// </summary>
    public bool? IsEncrypted { get; }

    /// <summary>
    /// Gets the timestamp when the audit entry was recorded.
    /// </summary>
    public UtcTimestamp OccurredUtc { get; }

    /// <summary>
    /// Creates an audit entry representing content changes.
    /// </summary>
    public static FileSystemAuditEntity ContentChanged(
        Guid fileSystemId,
        StoragePath path,
        FileHash hash,
        ByteSize size,
        MimeType mime,
        bool isEncrypted,
        UtcTimestamp occurredUtc)
    {
        return new FileSystemAuditEntity(
            fileSystemId,
            FileSystemAuditAction.ContentChanged,
            path.Value,
            hash.Value,
            size.Value,
            mime.Value,
            attributes: null,
            ownerSid: null,
            isEncrypted,
            occurredUtc);
    }

    /// <summary>
    /// Creates an audit entry representing a move to a different path.
    /// </summary>
    public static FileSystemAuditEntity Moved(
        Guid fileSystemId,
        StoragePath newPath,
        UtcTimestamp occurredUtc)
    {
        return new FileSystemAuditEntity(
            fileSystemId,
            FileSystemAuditAction.Moved,
            newPath.Value,
            hash: null,
            size: null,
            mime: null,
            attributes: null,
            ownerSid: null,
            isEncrypted: null,
            occurredUtc);
    }

    /// <summary>
    /// Creates an audit entry representing attribute updates.
    /// </summary>
    public static FileSystemAuditEntity AttributesChanged(
        Guid fileSystemId,
        FileAttributesFlags attributes,
        UtcTimestamp occurredUtc)
    {
        return new FileSystemAuditEntity(
            fileSystemId,
            FileSystemAuditAction.AttributesChanged,
            path: null,
            hash: null,
            size: null,
            mime: null,
            attributes: (int)attributes,
            ownerSid: null,
            isEncrypted: null,
            occurredUtc);
    }

    /// <summary>
    /// Creates an audit entry representing owner changes.
    /// </summary>
    public static FileSystemAuditEntity OwnerChanged(
        Guid fileSystemId,
        string? ownerSid,
        UtcTimestamp occurredUtc)
    {
        return new FileSystemAuditEntity(
            fileSystemId,
            FileSystemAuditAction.OwnerChanged,
            path: null,
            hash: null,
            size: null,
            mime: null,
            attributes: null,
            ownerSid,
            isEncrypted: null,
            occurredUtc);
    }

    /// <summary>
    /// Creates an audit entry representing timestamp updates.
    /// </summary>
    public static FileSystemAuditEntity TimestampsUpdated(Guid fileSystemId, UtcTimestamp occurredUtc)
    {
        return new FileSystemAuditEntity(
            fileSystemId,
            FileSystemAuditAction.TimestampsUpdated,
            path: null,
            hash: null,
            size: null,
            mime: null,
            attributes: null,
            ownerSid: null,
            isEncrypted: null,
            occurredUtc);
    }

    /// <summary>
    /// Creates an audit entry representing detection of missing content.
    /// </summary>
    public static FileSystemAuditEntity MissingDetected(
        Guid fileSystemId,
        StoragePath path,
        UtcTimestamp occurredUtc)
    {
        return new FileSystemAuditEntity(
            fileSystemId,
            FileSystemAuditAction.MissingDetected,
            path.Value,
            hash: null,
            size: null,
            mime: null,
            attributes: null,
            ownerSid: null,
            isEncrypted: null,
            occurredUtc);
    }

    /// <summary>
    /// Creates an audit entry representing rehydration of content.
    /// </summary>
    public static FileSystemAuditEntity Rehydrated(
        Guid fileSystemId,
        StoragePath path,
        UtcTimestamp occurredUtc)
    {
        return new FileSystemAuditEntity(
            fileSystemId,
            FileSystemAuditAction.Rehydrated,
            path.Value,
            hash: null,
            size: null,
            mime: null,
            attributes: null,
            ownerSid: null,
            isEncrypted: null,
            occurredUtc);
    }
}

/// <summary>
/// Enumerates supported audit actions for file system level events.
/// </summary>
public enum FileSystemAuditAction
{
    /// <summary>
    /// Indicates that the content metadata changed.
    /// </summary>
    ContentChanged,

    /// <summary>
    /// Indicates that the storage path changed.
    /// </summary>
    Moved,

    /// <summary>
    /// Indicates that file attributes changed.
    /// </summary>
    AttributesChanged,

    /// <summary>
    /// Indicates that the owner SID changed.
    /// </summary>
    OwnerChanged,

    /// <summary>
    /// Indicates that timestamps were updated.
    /// </summary>
    TimestampsUpdated,

    /// <summary>
    /// Indicates that the content was detected as missing.
    /// </summary>
    MissingDetected,

    /// <summary>
    /// Indicates that the content was rehydrated.
    /// </summary>
    Rehydrated,
}
