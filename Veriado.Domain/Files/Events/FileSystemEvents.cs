using System;
using Veriado.Domain.Files;
using Veriado.Domain.Metadata;
using Veriado.Domain.Primitives;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.Files.Events;

/// <summary>
/// Domain event emitted when the physical file content is created or replaced.
/// </summary>
/// <param name="FileSystemId">The identifier of the file system entity.</param>
/// <param name="Hash">The SHA-256 hash of the persisted content.</param>
/// <param name="Size">The size of the content in bytes.</param>
/// <param name="Mime">The MIME type describing the content.</param>
/// <param name="ContentVersion">The version assigned to the persisted content.</param>
/// <param name="Provider">The storage provider hosting the file.</param>
/// <param name="StoragePath">The normalized storage path for the content.</param>
/// <param name="IsEncrypted">Indicates whether the content is encrypted at rest.</param>
/// <param name="IsMissing">Indicates whether the content is currently missing.</param>
/// <param name="OccurredUtc">The timestamp when the change occurred.</param>
public sealed record FileSystemContentChanged(
    Guid FileSystemId,
    FileHash Hash,
    ByteSize Size,
    MimeType Mime,
    int ContentVersion,
    StorageProvider Provider,
    string StoragePath,
    bool IsEncrypted,
    bool IsMissing,
    UtcTimestamp OccurredUtc) : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; } = Guid.NewGuid();

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; } = OccurredUtc.ToDateTimeOffset();
}

/// <summary>
/// Domain event emitted when a file's storage path changes.
/// </summary>
/// <param name="FileSystemId">The identifier of the file system entity.</param>
/// <param name="PreviousPath">The previous storage path.</param>
/// <param name="NewPath">The new storage path.</param>
/// <param name="OccurredUtc">The timestamp when the move occurred.</param>
public sealed record FileSystemMoved(
    Guid FileSystemId,
    string PreviousPath,
    string NewPath,
    UtcTimestamp OccurredUtc) : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; } = Guid.NewGuid();

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; } = OccurredUtc.ToDateTimeOffset();
}

/// <summary>
/// Domain event emitted when file attributes are updated.
/// </summary>
/// <param name="FileSystemId">The identifier of the file system entity.</param>
/// <param name="Attributes">The updated attribute flags.</param>
/// <param name="OccurredUtc">The timestamp when the change occurred.</param>
public sealed record FileSystemAttributesChanged(
    Guid FileSystemId,
    FileAttributesFlags Attributes,
    UtcTimestamp OccurredUtc) : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; } = Guid.NewGuid();

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; } = OccurredUtc.ToDateTimeOffset();
}

/// <summary>
/// Domain event emitted when a file owner SID changes.
/// </summary>
/// <param name="FileSystemId">The identifier of the file system entity.</param>
/// <param name="OwnerSid">The normalized owner SID.</param>
/// <param name="OccurredUtc">The timestamp when the change occurred.</param>
public sealed record FileSystemOwnerChanged(
    Guid FileSystemId,
    string? OwnerSid,
    UtcTimestamp OccurredUtc) : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; } = Guid.NewGuid();

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; } = OccurredUtc.ToDateTimeOffset();
}

/// <summary>
/// Domain event emitted when file system timestamps are updated.
/// </summary>
/// <param name="FileSystemId">The identifier of the file system entity.</param>
/// <param name="CreatedUtc">The updated creation timestamp.</param>
/// <param name="LastWriteUtc">The updated last write timestamp.</param>
/// <param name="LastAccessUtc">The updated last access timestamp.</param>
/// <param name="OccurredUtc">The timestamp when the update occurred.</param>
public sealed record FileSystemTimestampsUpdated(
    Guid FileSystemId,
    UtcTimestamp CreatedUtc,
    UtcTimestamp LastWriteUtc,
    UtcTimestamp LastAccessUtc,
    UtcTimestamp OccurredUtc) : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; } = Guid.NewGuid();

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; } = OccurredUtc.ToDateTimeOffset();
}

/// <summary>
/// Domain event emitted when content is detected as missing from storage.
/// </summary>
/// <param name="FileSystemId">The identifier of the file system entity.</param>
/// <param name="StoragePath">The storage path that failed validation.</param>
/// <param name="OccurredUtc">The timestamp when the missing state was detected.</param>
public sealed record FileSystemMissingDetected(
    Guid FileSystemId,
    string StoragePath,
    UtcTimestamp OccurredUtc) : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; } = Guid.NewGuid();

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; } = OccurredUtc.ToDateTimeOffset();
}

/// <summary>
/// Domain event emitted when missing content is restored to storage.
/// </summary>
/// <param name="FileSystemId">The identifier of the file system entity.</param>
/// <param name="StoragePath">The path where the content is now available.</param>
/// <param name="OccurredUtc">The timestamp when the rehydration occurred.</param>
public sealed record FileSystemRehydrated(
    Guid FileSystemId,
    string StoragePath,
    UtcTimestamp OccurredUtc) : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; } = Guid.NewGuid();

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; } = OccurredUtc.ToDateTimeOffset();
}
