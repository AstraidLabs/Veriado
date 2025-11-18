using Veriado.Domain.Metadata;
using Veriado.Domain.Primitives;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.FileSystem.Events;

/// <summary>
/// Domain event emitted when the physical content backing a file is created or replaced.
/// </summary>
public sealed class FileSystemContentChanged : IDomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemContentChanged"/> class.
    /// </summary>
    public FileSystemContentChanged(
        Guid fileSystemId,
        StorageProvider provider,
        RelativeFilePath path,
        FileHash hash,
        ByteSize size,
        MimeType mime,
        ContentVersion contentVersion,
        bool isEncrypted,
        UtcTimestamp occurredUtc)
    {
        FileSystemId = fileSystemId;
        Provider = provider;
        Path = path;
        Hash = hash;
        Size = size;
        Mime = mime;
        ContentVersion = contentVersion;
        IsEncrypted = isEncrypted;
        EventId = Guid.NewGuid();
        OccurredOnUtc = occurredUtc.ToDateTimeOffset();
    }

    /// <summary>
    /// Gets the identifier of the file system aggregate.
    /// </summary>
    public Guid FileSystemId { get; }

    /// <summary>
    /// Gets the provider hosting the content.
    /// </summary>
    public StorageProvider Provider { get; }

    /// <summary>
    /// Gets the relative path referencing the content within the storage root.
    /// </summary>
    public RelativeFilePath Path { get; }

    /// <summary>
    /// Gets the hash of the content.
    /// </summary>
    public FileHash Hash { get; }

    /// <summary>
    /// Gets the size of the content.
    /// </summary>
    public ByteSize Size { get; }

    /// <summary>
    /// Gets the MIME type of the content.
    /// </summary>
    public MimeType Mime { get; }

    /// <summary>
    /// Gets the version assigned to the content.
    /// </summary>
    public ContentVersion ContentVersion { get; }

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
/// Domain event emitted when the physical content is moved to a different path.
/// </summary>
public sealed class FileSystemMoved : IDomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemMoved"/> class.
    /// </summary>
    public FileSystemMoved(
        Guid fileSystemId,
        StorageProvider provider,
        RelativeFilePath previousPath,
        RelativeFilePath newPath,
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
    /// Gets the identifier of the file system aggregate.
    /// </summary>
    public Guid FileSystemId { get; }

    /// <summary>
    /// Gets the storage provider hosting the content.
    /// </summary>
    public StorageProvider Provider { get; }

    /// <summary>
    /// Gets the previous relative storage path.
    /// </summary>
    public RelativeFilePath PreviousPath { get; }

    /// <summary>
    /// Gets the new relative storage path.
    /// </summary>
    public RelativeFilePath NewPath { get; }

    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; }
}

/// <summary>
/// Domain event emitted when file attributes change.
/// </summary>
public sealed class FileSystemAttributesChanged : IDomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemAttributesChanged"/> class.
    /// </summary>
    public FileSystemAttributesChanged(Guid fileSystemId, FileAttributesFlags attributes, UtcTimestamp occurredUtc)
    {
        FileSystemId = fileSystemId;
        Attributes = attributes;
        EventId = Guid.NewGuid();
        OccurredOnUtc = occurredUtc.ToDateTimeOffset();
    }

    /// <summary>
    /// Gets the identifier of the file system aggregate.
    /// </summary>
    public Guid FileSystemId { get; }

    /// <summary>
    /// Gets the updated attributes.
    /// </summary>
    public FileAttributesFlags Attributes { get; }

    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; }
}

/// <summary>
/// Domain event emitted when the owner SID of a file changes.
/// </summary>
public sealed class FileSystemOwnerChanged : IDomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemOwnerChanged"/> class.
    /// </summary>
    public FileSystemOwnerChanged(Guid fileSystemId, string? ownerSid, UtcTimestamp occurredUtc)
    {
        FileSystemId = fileSystemId;
        OwnerSid = ownerSid;
        EventId = Guid.NewGuid();
        OccurredOnUtc = occurredUtc.ToDateTimeOffset();
    }

    /// <summary>
    /// Gets the identifier of the file system aggregate.
    /// </summary>
    public Guid FileSystemId { get; }

    /// <summary>
    /// Gets the owner SID.
    /// </summary>
    public string? OwnerSid { get; }

    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; }
}

/// <summary>
/// Domain event emitted when timestamps are updated.
/// </summary>
public sealed class FileSystemTimestampsUpdated : IDomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemTimestampsUpdated"/> class.
    /// </summary>
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
    /// Gets the identifier of the file system aggregate.
    /// </summary>
    public Guid FileSystemId { get; }

    /// <summary>
    /// Gets the creation timestamp.
    /// </summary>
    public UtcTimestamp CreatedUtc { get; }

    /// <summary>
    /// Gets the last write timestamp.
    /// </summary>
    public UtcTimestamp LastWriteUtc { get; }

    /// <summary>
    /// Gets the last access timestamp.
    /// </summary>
    public UtcTimestamp LastAccessUtc { get; }

    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; }
}

/// <summary>
/// Domain event emitted when content is detected as missing.
/// </summary>
public sealed class FileSystemMissingDetected : IDomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemMissingDetected"/> class.
    /// </summary>
    public FileSystemMissingDetected(
        Guid fileSystemId,
        RelativeFilePath path,
        UtcTimestamp occurredUtc,
        UtcTimestamp? missingSinceUtc)
    {
        FileSystemId = fileSystemId;
        Path = path;
        MissingSinceUtc = missingSinceUtc;
        EventId = Guid.NewGuid();
        OccurredOnUtc = occurredUtc.ToDateTimeOffset();
    }

    /// <summary>
    /// Gets the identifier of the file system aggregate.
    /// </summary>
    public Guid FileSystemId { get; }

    /// <summary>
    /// Gets the relative path that was probed.
    /// </summary>
    public RelativeFilePath Path { get; }

    /// <summary>
    /// Gets the timestamp when the content was first observed missing, if known.
    /// </summary>
    public UtcTimestamp? MissingSinceUtc { get; }

    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; }
}

/// <summary>
/// Domain event emitted when content is rehydrated in storage.
/// </summary>
public sealed class FileSystemRehydrated : IDomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemRehydrated"/> class.
    /// </summary>
    public FileSystemRehydrated(
        Guid fileSystemId,
        StorageProvider provider,
        RelativeFilePath path,
        bool wasMissing,
        UtcTimestamp? missingSinceUtc,
        UtcTimestamp occurredUtc)
    {
        FileSystemId = fileSystemId;
        Provider = provider;
        Path = path;
        WasMissing = wasMissing;
        MissingSinceUtc = missingSinceUtc;
        EventId = Guid.NewGuid();
        OccurredOnUtc = occurredUtc.ToDateTimeOffset();
    }

    /// <summary>
    /// Gets the identifier of the file system aggregate.
    /// </summary>
    public Guid FileSystemId { get; }

    /// <summary>
    /// Gets the provider hosting the content.
    /// </summary>
    public StorageProvider Provider { get; }

    /// <summary>
    /// Gets the relative path referencing the rehydrated content.
    /// </summary>
    public RelativeFilePath Path { get; }

    /// <summary>
    /// Gets a value indicating whether the content had been missing prior to rehydration.
    /// </summary>
    public bool WasMissing { get; }

    /// <summary>
    /// Gets the timestamp when the content was first observed missing, if known.
    /// </summary>
    public UtcTimestamp? MissingSinceUtc { get; }

    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; }
}
