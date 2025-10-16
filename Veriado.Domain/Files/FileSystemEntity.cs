using System;
using Veriado.Domain.Files.Events;
using Veriado.Domain.Metadata;
using Veriado.Domain.Primitives;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.Files;

/// <summary>
/// Represents metadata for a physical file stored on an external storage provider.
/// </summary>
public sealed class FileSystemEntity : AggregateRoot
{
    private const int InitialVersion = 1;

    private FileSystemEntity(Guid id)
        : base(id)
    {
    }

    private FileSystemEntity(
        Guid id,
        StorageProvider provider,
        string storagePath,
        MimeType mime,
        FileHash hash,
        ByteSize size,
        FileAttributesFlags attributes,
        UtcTimestamp createdUtc,
        UtcTimestamp lastWriteUtc,
        UtcTimestamp lastAccessUtc,
        string? ownerSid,
        bool isEncrypted,
        bool isMissing,
        int contentVersion)
        : base(id)
    {
        Provider = provider;
        StoragePath = storagePath;
        Mime = mime;
        Hash = hash;
        Size = size;
        Attributes = attributes;
        CreatedUtc = createdUtc;
        LastWriteUtc = lastWriteUtc;
        LastAccessUtc = lastAccessUtc;
        OwnerSid = ownerSid;
        IsEncrypted = isEncrypted;
        IsMissing = isMissing;
        ContentVersion = contentVersion;
    }

    /// <summary>
    /// Gets the storage provider hosting the file.
    /// </summary>
    public StorageProvider Provider { get; private set; }

    /// <summary>
    /// Gets the normalized storage path of the file within the provider.
    /// </summary>
    public string StoragePath { get; private set; } = null!;

    /// <summary>
    /// Gets the MIME type associated with the file content.
    /// </summary>
    public MimeType Mime { get; private set; }

    /// <summary>
    /// Gets the SHA-256 hash of the persisted content.
    /// </summary>
    public FileHash Hash { get; private set; }

    /// <summary>
    /// Gets the size of the stored content in bytes.
    /// </summary>
    public ByteSize Size { get; private set; }

    /// <summary>
    /// Gets the current file attribute flags captured for the file.
    /// </summary>
    public FileAttributesFlags Attributes { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the file was created on disk.
    /// </summary>
    public UtcTimestamp CreatedUtc { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp of the last write operation on disk.
    /// </summary>
    public UtcTimestamp LastWriteUtc { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp of the last access operation on disk.
    /// </summary>
    public UtcTimestamp LastAccessUtc { get; private set; }

    /// <summary>
    /// Gets the normalized security identifier (SID) of the owning principal, if available.
    /// </summary>
    public string? OwnerSid { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the file content is encrypted at rest.
    /// </summary>
    public bool IsEncrypted { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the file content is currently missing from storage.
    /// </summary>
    public bool IsMissing { get; private set; }

    /// <summary>
    /// Gets the version number of the persisted content.
    /// </summary>
    public int ContentVersion { get; private set; }

    /// <summary>
    /// Creates a new <see cref="FileSystemEntity"/> aggregate from binary content.
    /// </summary>
    /// <param name="provider">The storage provider hosting the file.</param>
    /// <param name="mime">The MIME type of the content.</param>
    /// <param name="bytes">The binary content to persist.</param>
    /// <param name="save">The delegate responsible for persisting the content and returning the storage path.</param>
    /// <param name="attributes">The file attribute flags.</param>
    /// <param name="createdUtc">The creation timestamp.</param>
    /// <param name="ownerSid">The optional owner SID.</param>
    /// <param name="isEncrypted">Indicates whether the content is encrypted at rest.</param>
    /// <param name="maxContentSize">Optional maximum allowed content size.</param>
    /// <returns>The created aggregate root.</returns>
    public static FileSystemEntity CreateNew(
        StorageProvider provider,
        MimeType mime,
        ReadOnlySpan<byte> bytes,
        Func<FileHash, ReadOnlySpan<byte>, string> save,
        FileAttributesFlags attributes,
        UtcTimestamp createdUtc,
        string? ownerSid,
        bool isEncrypted,
        int? maxContentSize = null)
    {
        ArgumentNullException.ThrowIfNull(save);
        EnsureContentSizeWithinLimit(maxContentSize, bytes.Length);

        var hash = FileHash.Compute(bytes);
        var storagePath = NormalizeStoragePath(save(hash, bytes));
        var size = ByteSize.From(bytes.Length);
        var normalizedOwner = NormalizeOwner(ownerSid);

        var entity = new FileSystemEntity(
            Guid.NewGuid(),
            provider,
            storagePath,
            mime,
            hash,
            size,
            attributes,
            createdUtc,
            createdUtc,
            createdUtc,
            normalizedOwner,
            isEncrypted,
            isMissing: false,
            contentVersion: InitialVersion);

        entity.RaiseDomainEvent(new FileSystemContentChanged(
            entity.Id,
            hash,
            size,
            mime,
            entity.ContentVersion,
            provider,
            storagePath,
            isEncrypted,
            entity.IsMissing,
            createdUtc));

        return entity;
    }

    /// <summary>
    /// Replaces the stored content with the provided bytes.
    /// </summary>
    /// <param name="bytes">The new binary content.</param>
    /// <param name="mime">The MIME type associated with the new content.</param>
    /// <param name="save">The delegate responsible for persisting the content and returning the storage path.</param>
    /// <param name="whenUtc">The timestamp when the operation occurred.</param>
    /// <param name="maxContentSize">Optional maximum allowed content size.</param>
    public void ReplaceContent(
        ReadOnlySpan<byte> bytes,
        MimeType mime,
        Func<FileHash, ReadOnlySpan<byte>, string> save,
        UtcTimestamp whenUtc,
        int? maxContentSize = null)
    {
        ArgumentNullException.ThrowIfNull(save);
        EnsureContentSizeWithinLimit(maxContentSize, bytes.Length);

        var hash = FileHash.Compute(bytes);
        var size = ByteSize.From(bytes.Length);

        if (hash == Hash && mime == Mime && !IsMissing)
        {
            return;
        }

        var storagePath = NormalizeStoragePath(save(hash, bytes));

        Hash = hash;
        Size = size;
        Mime = mime;
        StoragePath = storagePath;
        IsMissing = false;
        ContentVersion += 1;
        LastWriteUtc = whenUtc;
        LastAccessUtc = whenUtc;

        RaiseDomainEvent(new FileSystemContentChanged(
            Id,
            Hash,
            Size,
            Mime,
            ContentVersion,
            Provider,
            StoragePath,
            IsEncrypted,
            IsMissing,
            whenUtc));
    }

    /// <summary>
    /// Updates the storage path of the file without modifying its content.
    /// </summary>
    /// <param name="newPath">The new storage path.</param>
    /// <param name="whenUtc">The timestamp when the move occurred.</param>
    public void MoveTo(string newPath, UtcTimestamp whenUtc)
    {
        var normalized = NormalizeStoragePath(newPath);
        if (string.Equals(StoragePath, normalized, StringComparison.Ordinal))
        {
            return;
        }

        var previous = StoragePath;
        StoragePath = normalized;
        LastWriteUtc = whenUtc;

        RaiseDomainEvent(new FileSystemMoved(Id, previous, normalized, whenUtc));
    }

    /// <summary>
    /// Updates the attribute flags describing the file on disk.
    /// </summary>
    /// <param name="attributes">The new attribute flags.</param>
    /// <param name="whenUtc">The timestamp when the change occurred.</param>
    public void UpdateAttributes(FileAttributesFlags attributes, UtcTimestamp whenUtc)
    {
        if (Attributes == attributes)
        {
            return;
        }

        Attributes = attributes;
        RaiseDomainEvent(new FileSystemAttributesChanged(Id, attributes, whenUtc));
    }

    /// <summary>
    /// Updates the owner security identifier associated with the file.
    /// </summary>
    /// <param name="ownerSid">The new owner SID.</param>
    /// <param name="whenUtc">The timestamp when the change occurred.</param>
    public void UpdateOwner(string? ownerSid, UtcTimestamp whenUtc)
    {
        var normalized = NormalizeOwner(ownerSid);
        if (string.Equals(OwnerSid, normalized, StringComparison.Ordinal))
        {
            return;
        }

        OwnerSid = normalized;
        RaiseDomainEvent(new FileSystemOwnerChanged(Id, OwnerSid, whenUtc));
    }

    /// <summary>
    /// Updates the file system timestamps recorded for the file.
    /// </summary>
    /// <param name="created">The optional updated creation timestamp.</param>
    /// <param name="lastWrite">The optional updated last write timestamp.</param>
    /// <param name="lastAccess">The optional updated last access timestamp.</param>
    /// <param name="whenUtc">The timestamp when the update occurred.</param>
    public void UpdateTimestamps(
        UtcTimestamp? created,
        UtcTimestamp? lastWrite,
        UtcTimestamp? lastAccess,
        UtcTimestamp whenUtc)
    {
        var changed = false;

        if (created.HasValue && created.Value != CreatedUtc)
        {
            CreatedUtc = created.Value;
            changed = true;
        }

        if (lastWrite.HasValue && lastWrite.Value != LastWriteUtc)
        {
            LastWriteUtc = lastWrite.Value;
            changed = true;
        }

        if (lastAccess.HasValue && lastAccess.Value != LastAccessUtc)
        {
            LastAccessUtc = lastAccess.Value;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        RaiseDomainEvent(new FileSystemTimestampsUpdated(Id, CreatedUtc, LastWriteUtc, LastAccessUtc, whenUtc));
    }

    /// <summary>
    /// Marks the file content as missing from the storage provider.
    /// </summary>
    /// <param name="whenUtc">The timestamp when the missing condition was detected.</param>
    public void MarkMissing(UtcTimestamp whenUtc)
    {
        if (IsMissing)
        {
            return;
        }

        IsMissing = true;
        RaiseDomainEvent(new FileSystemMissingDetected(Id, StoragePath, whenUtc));
    }

    /// <summary>
    /// Rehydrates the file metadata after the content is restored.
    /// </summary>
    /// <param name="newPath">An optional new path if the file was restored to a different location.</param>
    /// <param name="whenUtc">The timestamp when the rehydration occurred.</param>
    public void Rehydrate(string? newPath, UtcTimestamp whenUtc)
    {
        var normalized = newPath is not null ? NormalizeStoragePath(newPath) : null;

        if (!IsMissing && (normalized is null || string.Equals(StoragePath, normalized, StringComparison.Ordinal)))
        {
            return;
        }

        if (normalized is not null)
        {
            StoragePath = normalized;
        }

        IsMissing = false;
        LastAccessUtc = whenUtc;

        RaiseDomainEvent(new FileSystemRehydrated(Id, StoragePath, whenUtc));
    }

    private static void EnsureContentSizeWithinLimit(int? maxContentSize, int length)
    {
        if (maxContentSize.HasValue && maxContentSize.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxContentSize), maxContentSize.Value, "Maximum content size must be non-negative.");
        }

        if (maxContentSize.HasValue && length > maxContentSize.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "Content exceeds the maximum allowed size.");
        }
    }

    private static string NormalizeStoragePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Storage path cannot be null or whitespace.", nameof(path));
        }

        var trimmed = path.Trim();
        var normalized = trimmed.Replace('\', '/');
        return normalized;
    }

    private static string? NormalizeOwner(string? ownerSid)
    {
        if (string.IsNullOrWhiteSpace(ownerSid))
        {
            return null;
        }

        return ownerSid.Trim().ToUpperInvariant();
    }
}

/// <summary>
/// Identifies the storage provider responsible for managing physical file content.
/// </summary>
public enum StorageProvider
{
    /// <summary>
    /// Local filesystem storage.
    /// </summary>
    Local,

    /// <summary>
    /// Network share storage.
    /// </summary>
    NetworkShare,
}
