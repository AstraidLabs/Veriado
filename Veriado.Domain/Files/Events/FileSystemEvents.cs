using Veriado.Domain.Files;
using Veriado.Domain.Metadata;
using Veriado.Domain.Primitives;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.Files.Events;

/// <summary>
/// Domain event emitted when file system content is created or replaced.
/// </summary>
public sealed class FileSystemContentChanged : IDomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemContentChanged"/> class.
    /// </summary>
    /// <param name="fileSystemId">The identifier of the file system entity.</param>
    /// <param name="provider">The storage provider hosting the content.</param>
    /// <param name="storagePath">The storage path referencing the binary content.</param>
    /// <param name="hash">The SHA-256 hash of the content.</param>
    /// <param name="size">The size of the content in bytes.</param>
    /// <param name="mime">The MIME type of the content.</param>
    /// <param name="contentVersion">The version number assigned to the content.</param>
    /// <param name="isEncrypted">Indicates whether the content is encrypted.</param>
    /// <param name="occurredUtc">The timestamp when the change was observed.</param>
    public FileSystemContentChanged(
        Guid fileSystemId,
        StorageProvider provider,
        string storagePath,
        FileHash hash,
        ByteSize size,
        MimeType mime,
        int contentVersion,
        bool isEncrypted,
        UtcTimestamp occurredUtc)
    {
        FileSystemId = fileSystemId;
        Provider = provider;
        StoragePath = storagePath;
        Hash = hash;
        Size = size;
        Mime = mime;
        ContentVersion = contentVersion;
        IsEncrypted = isEncrypted;
        EventId = Guid.NewGuid();
        OccurredOnUtc = occurredUtc.ToDateTimeOffset();
    }

    /// <summary>
    /// Gets the identifier of the file system entity.
    /// </summary>
    public Guid FileSystemId { get; }

    /// <summary>
    /// Gets the storage provider hosting the content.
    /// </summary>
    public StorageProvider Provider { get; }

    /// <summary>
    /// Gets the storage path referencing the binary content.
    /// </summary>
    public string StoragePath { get; }

    /// <summary>
    /// Gets the SHA-256 hash of the content.
    /// </summary>
    public FileHash Hash { get; }

    /// <summary>
    /// Gets the size of the content in bytes.
    /// </summary>
    public ByteSize Size { get; }

    /// <summary>
    /// Gets the MIME type of the content.
    /// </summary>
    public MimeType Mime { get; }

    /// <summary>
    /// Gets the version number assigned to the content.
    /// </summary>
    public int ContentVersion { get; }

    /// <summary>
    /// Gets a value indicating whether the content is encrypted.
    /// </summary>
    public bool IsEncrypted { get; }

    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; }
}

/// <summary>
/// Domain event emitted when file system content is moved to a different storage path.
/// </summary>
public sealed class FileSystemMoved : IDomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemMoved"/> class.
    /// </summary>
    /// <param name="fileSystemId">The identifier of the file system entity.</param>
    /// <param name="provider">The storage provider hosting the content.</param>
    /// <param name="previousPath">The previous storage path.</param>
    /// <param name="newPath">The new storage path.</param>
    /// <param name="occurredUtc">The timestamp when the change was observed.</param>
    public FileSystemMoved(
        Guid fileSystemId,
        StorageProvider provider,
        string previousPath,
        string newPath,
        UtcTimestamp occurredUtc)
    {
        FileSystemId = fileSystemId;
        Provider = provider;
        PreviousPath = previousPath;
        NewPath = newPath;
        EventId = Guid.NewGuid();
        OccurredOnUtc = occurredUtc.ToDateTimeOffset();
    }

    /// <summary>
    /// Gets the identifier of the file system entity.
    /// </summary>
    public Guid FileSystemId { get; }

    /// <summary>
    /// Gets the storage provider hosting the content.
    /// </summary>
    public StorageProvider Provider { get; }

    /// <summary>
    /// Gets the previous storage path.
    /// </summary>
    public string PreviousPath { get; }

    /// <summary>
    /// Gets the new storage path.
    /// </summary>
    public string NewPath { get; }

    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; }
}

/// <summary>
/// Domain event emitted when file system attributes are updated.
/// </summary>
public sealed class FileSystemAttributesChanged : IDomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemAttributesChanged"/> class.
    /// </summary>
    /// <param name="fileSystemId">The identifier of the file system entity.</param>
    /// <param name="attributes">The updated file system attributes.</param>
    /// <param name="occurredUtc">The timestamp when the change was observed.</param>
    public FileSystemAttributesChanged(Guid fileSystemId, FileAttributesFlags attributes, UtcTimestamp occurredUtc)
    {
        FileSystemId = fileSystemId;
        Attributes = attributes;
        EventId = Guid.NewGuid();
        OccurredOnUtc = occurredUtc.ToDateTimeOffset();
    }

    /// <summary>
    /// Gets the identifier of the file system entity.
    /// </summary>
    public Guid FileSystemId { get; }

    /// <summary>
    /// Gets the updated file system attributes.
    /// </summary>
    public FileAttributesFlags Attributes { get; }

    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; }
}

/// <summary>
/// Domain event emitted when the owner SID associated with a file is updated.
/// </summary>
public sealed class FileSystemOwnerChanged : IDomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemOwnerChanged"/> class.
    /// </summary>
    /// <param name="fileSystemId">The identifier of the file system entity.</param>
    /// <param name="ownerSid">The new owner SID.</param>
    /// <param name="occurredUtc">The timestamp when the change was observed.</param>
    public FileSystemOwnerChanged(Guid fileSystemId, string? ownerSid, UtcTimestamp occurredUtc)
    {
        FileSystemId = fileSystemId;
        OwnerSid = ownerSid;
        EventId = Guid.NewGuid();
        OccurredOnUtc = occurredUtc.ToDateTimeOffset();
    }

    /// <summary>
    /// Gets the identifier of the file system entity.
    /// </summary>
    public Guid FileSystemId { get; }

    /// <summary>
    /// Gets the owner SID associated with the file.
    /// </summary>
    public string? OwnerSid { get; }

    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; }
}

/// <summary>
/// Domain event emitted when file system timestamps are updated.
/// </summary>
public sealed class FileSystemTimestampsUpdated : IDomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemTimestampsUpdated"/> class.
    /// </summary>
    /// <param name="fileSystemId">The identifier of the file system entity.</param>
    /// <param name="createdUtc">The updated creation timestamp.</param>
    /// <param name="lastWriteUtc">The updated last write timestamp.</param>
    /// <param name="lastAccessUtc">The updated last access timestamp.</param>
    /// <param name="occurredUtc">The timestamp when the change was observed.</param>
    public FileSystemTimestampsUpdated(
        Guid fileSystemId,
        UtcTimestamp createdUtc,
        UtcTimestamp lastWriteUtc,
        UtcTimestamp lastAccessUtc,
        UtcTimestamp occurredUtc)
    {
        FileSystemId = fileSystemId;
        CreatedUtc = createdUtc;
        LastWriteUtc = lastWriteUtc;
        LastAccessUtc = lastAccessUtc;
        EventId = Guid.NewGuid();
        OccurredOnUtc = occurredUtc.ToDateTimeOffset();
    }

    /// <summary>
    /// Gets the identifier of the file system entity.
    /// </summary>
    public Guid FileSystemId { get; }

    /// <summary>
    /// Gets the updated creation timestamp.
    /// </summary>
    public UtcTimestamp CreatedUtc { get; }

    /// <summary>
    /// Gets the updated last write timestamp.
    /// </summary>
    public UtcTimestamp LastWriteUtc { get; }

    /// <summary>
    /// Gets the updated last access timestamp.
    /// </summary>
    public UtcTimestamp LastAccessUtc { get; }

    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; }
}

/// <summary>
/// Domain event emitted when a file is detected as missing from storage.
/// </summary>
public sealed class FileSystemMissingDetected : IDomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemMissingDetected"/> class.
    /// </summary>
    /// <param name="fileSystemId">The identifier of the file system entity.</param>
    /// <param name="storagePath">The storage path that was probed.</param>
    /// <param name="occurredUtc">The timestamp when the missing state was detected.</param>
    public FileSystemMissingDetected(Guid fileSystemId, string storagePath, UtcTimestamp occurredUtc)
    {
        FileSystemId = fileSystemId;
        StoragePath = storagePath;
        EventId = Guid.NewGuid();
        OccurredOnUtc = occurredUtc.ToDateTimeOffset();
    }

    /// <summary>
    /// Gets the identifier of the file system entity.
    /// </summary>
    public Guid FileSystemId { get; }

    /// <summary>
    /// Gets the storage path that was probed.
    /// </summary>
    public string StoragePath { get; }

    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; }
}

/// <summary>
/// Domain event emitted when a previously missing file is rehydrated in storage.
/// </summary>
public sealed class FileSystemRehydrated : IDomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemRehydrated"/> class.
    /// </summary>
    /// <param name="fileSystemId">The identifier of the file system entity.</param>
    /// <param name="provider">The storage provider hosting the content.</param>
    /// <param name="storagePath">The storage path referencing the binary content.</param>
    /// <param name="wasMissing">Indicates whether the file was previously marked as missing.</param>
    /// <param name="occurredUtc">The timestamp when rehydration occurred.</param>
    public FileSystemRehydrated(
        Guid fileSystemId,
        StorageProvider provider,
        string storagePath,
        bool wasMissing,
        UtcTimestamp occurredUtc)
    {
        FileSystemId = fileSystemId;
        Provider = provider;
        StoragePath = storagePath;
        WasMissing = wasMissing;
        EventId = Guid.NewGuid();
        OccurredOnUtc = occurredUtc.ToDateTimeOffset();
    }

    /// <summary>
    /// Gets the identifier of the file system entity.
    /// </summary>
    public Guid FileSystemId { get; }

    /// <summary>
    /// Gets the storage provider hosting the content.
    /// </summary>
    public StorageProvider Provider { get; }

    /// <summary>
    /// Gets the storage path referencing the binary content.
    /// </summary>
    public string StoragePath { get; }

    /// <summary>
    /// Gets a value indicating whether the file was previously missing from storage.
    /// </summary>
    public bool WasMissing { get; }

    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; }
}
