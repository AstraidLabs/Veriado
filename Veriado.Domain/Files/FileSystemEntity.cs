using Veriado.Domain.Files.Events;
using Veriado.Domain.Metadata;
using Veriado.Domain.Primitives;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.Files;

/// <summary>
/// Represents the metadata for a physical file stored in an external storage system.
/// </summary>
public sealed class FileSystemEntity : AggregateRoot
{
    private const int InitialContentVersion = 1;

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
    /// Gets the storage provider hosting the file content.
    /// </summary>
    public StorageProvider Provider { get; private set; }

    /// <summary>
    /// Gets the storage path referencing the file content.
    /// </summary>
    public string StoragePath { get; private set; } = null!;

    /// <summary>
    /// Gets the MIME type associated with the file.
    /// </summary>
    public MimeType Mime { get; private set; }

    /// <summary>
    /// Gets the SHA-256 hash of the stored content.
    /// </summary>
    public FileHash Hash { get; private set; }

    /// <summary>
    /// Gets the file size in bytes.
    /// </summary>
    public ByteSize Size { get; private set; }

    /// <summary>
    /// Gets the file system attributes snapshot.
    /// </summary>
    public FileAttributesFlags Attributes { get; private set; }

    /// <summary>
    /// Gets the timestamp when the file was created.
    /// </summary>
    public UtcTimestamp CreatedUtc { get; private set; }

    /// <summary>
    /// Gets the timestamp when the file content was last modified.
    /// </summary>
    public UtcTimestamp LastWriteUtc { get; private set; }

    /// <summary>
    /// Gets the timestamp when the file was last accessed.
    /// </summary>
    public UtcTimestamp LastAccessUtc { get; private set; }

    /// <summary>
    /// Gets the optional owner SID associated with the file.
    /// </summary>
    public string? OwnerSid { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the file is encrypted at rest by the storage provider.
    /// </summary>
    public bool IsEncrypted { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the file is currently missing from the storage provider.
    /// </summary>
    public bool IsMissing { get; private set; }

    /// <summary>
    /// Gets the version number of the physical content stored for this file.
    /// </summary>
    public int ContentVersion { get; private set; }

    /// <summary>
    /// Creates a new file system entity from the provided content bytes and metadata.
    /// </summary>
    /// <param name="provider">The storage provider containing the file.</param>
    /// <param name="mime">The MIME type of the content.</param>
    /// <param name="bytes">The binary content to persist externally.</param>
    /// <param name="save">The delegate responsible for persisting the content and returning the storage path.</param>
    /// <param name="attributes">The file system attributes.</param>
    /// <param name="createdUtc">The creation timestamp.</param>
    /// <param name="lastWriteUtc">The last write timestamp.</param>
    /// <param name="lastAccessUtc">The last access timestamp.</param>
    /// <param name="ownerSid">The optional owner SID.</param>
    /// <param name="isEncrypted">Indicates whether the content is encrypted.</param>
    /// <param name="maxContentSize">Optional maximum content size in bytes.</param>
    /// <returns>The created aggregate root.</returns>
    public static FileSystemEntity CreateNew(
        StorageProvider provider,
        MimeType mime,
        ReadOnlySpan<byte> bytes,
        Func<FileHash, ReadOnlySpan<byte>, string> save,
        FileAttributesFlags attributes,
        UtcTimestamp createdUtc,
        UtcTimestamp lastWriteUtc,
        UtcTimestamp lastAccessUtc,
        string? ownerSid,
        bool isEncrypted,
        int? maxContentSize = null)
    {
        ArgumentNullException.ThrowIfNull(save);
        if (maxContentSize.HasValue && bytes.Length > maxContentSize.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes), bytes.Length, "Content exceeds the maximum allowed size.");
        }

        var hash = FileHash.Compute(bytes);
        var path = NormalizeStoragePath(save(hash, bytes));
        var size = ByteSize.From(bytes.Length);
        var normalizedOwner = NormalizeOwnerSid(ownerSid);

        var entity = new FileSystemEntity(
            Guid.NewGuid(),
            provider,
            path,
            mime,
            hash,
            size,
            attributes,
            createdUtc,
            lastWriteUtc,
            lastAccessUtc,
            normalizedOwner,
            isEncrypted,
            isMissing: false,
            contentVersion: InitialContentVersion);

        entity.RaiseDomainEvent(new FileSystemContentChanged(
            entity.Id,
            provider,
            path,
            hash,
            size,
            mime,
            entity.ContentVersion,
            isEncrypted,
            createdUtc));

        if (!string.IsNullOrEmpty(normalizedOwner))
        {
            entity.RaiseDomainEvent(new FileSystemOwnerChanged(entity.Id, normalizedOwner, createdUtc));
        }

        entity.RaiseDomainEvent(new FileSystemTimestampsUpdated(
            entity.Id,
            createdUtc,
            lastWriteUtc,
            lastAccessUtc,
            createdUtc));

        if (attributes != FileAttributesFlags.None)
        {
            entity.RaiseDomainEvent(new FileSystemAttributesChanged(entity.Id, attributes, createdUtc));
        }

        return entity;
    }

    /// <summary>
    /// Replaces the stored content with new bytes and updates metadata.
    /// </summary>
    /// <param name="bytes">The new binary content.</param>
    /// <param name="mime">The MIME type of the content.</param>
    /// <param name="save">The delegate responsible for persisting the content and returning the storage path.</param>
    /// <param name="whenUtc">The timestamp describing when the change occurred.</param>
    /// <param name="maxContentSize">Optional maximum content size in bytes.</param>
    public void ReplaceContent(
        ReadOnlySpan<byte> bytes,
        MimeType mime,
        Func<FileHash, ReadOnlySpan<byte>, string> save,
        UtcTimestamp whenUtc,
        int? maxContentSize = null)
    {
        ArgumentNullException.ThrowIfNull(save);
        if (maxContentSize.HasValue && bytes.Length > maxContentSize.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes), bytes.Length, "Content exceeds the maximum allowed size.");
        }

        var hash = FileHash.Compute(bytes);
        if (hash == Hash && mime == Mime && bytes.Length == Size.Value)
        {
            return;
        }

        var path = NormalizeStoragePath(save(hash, bytes));

        Hash = hash;
        Mime = mime;
        StoragePath = path;
        Size = ByteSize.From(bytes.Length);
        LastWriteUtc = whenUtc;
        IsMissing = false;
        ContentVersion += 1;

        RaiseDomainEvent(new FileSystemContentChanged(Id, Provider, path, hash, Size, Mime, ContentVersion, IsEncrypted, whenUtc));
    }

    /// <summary>
    /// Moves the file to a new storage path within the same provider.
    /// </summary>
    /// <param name="newPath">The new storage path.</param>
    /// <param name="whenUtc">The timestamp describing when the change occurred.</param>
    public void MoveTo(string newPath, UtcTimestamp whenUtc)
    {
        var normalized = NormalizeStoragePath(newPath);
        if (string.Equals(StoragePath, normalized, StringComparison.Ordinal))
        {
            return;
        }

        var previous = StoragePath;
        StoragePath = normalized;

        RaiseDomainEvent(new FileSystemMoved(Id, Provider, previous, normalized, whenUtc));
    }

    /// <summary>
    /// Updates the file attributes snapshot.
    /// </summary>
    /// <param name="attributes">The new attribute flags.</param>
    /// <param name="whenUtc">The timestamp describing when the change occurred.</param>
    public void UpdateAttributes(FileAttributesFlags attributes, UtcTimestamp whenUtc)
    {
        if (attributes == Attributes)
        {
            return;
        }

        Attributes = attributes;
        RaiseDomainEvent(new FileSystemAttributesChanged(Id, attributes, whenUtc));
    }

    /// <summary>
    /// Updates the stored owner SID reference.
    /// </summary>
    /// <param name="ownerSid">The new owner SID.</param>
    /// <param name="whenUtc">The timestamp describing when the change occurred.</param>
    public void UpdateOwner(string? ownerSid, UtcTimestamp whenUtc)
    {
        var normalized = NormalizeOwnerSid(ownerSid);
        if (string.Equals(OwnerSid, normalized, StringComparison.Ordinal))
        {
            return;
        }

        OwnerSid = normalized;
        RaiseDomainEvent(new FileSystemOwnerChanged(Id, normalized, whenUtc));
    }

    /// <summary>
    /// Updates the file system timestamps.
    /// </summary>
    /// <param name="created">Optional creation timestamp.</param>
    /// <param name="lastWrite">Optional last write timestamp.</param>
    /// <param name="lastAccess">Optional last access timestamp.</param>
    /// <param name="whenUtc">The timestamp describing when the change occurred.</param>
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
    /// Marks the file as missing in storage.
    /// </summary>
    /// <param name="whenUtc">The timestamp describing when the missing state was detected.</param>
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
    /// Rehydrates the file metadata when the content is restored in storage.
    /// </summary>
    /// <param name="newPath">An optional new storage path.</param>
    /// <param name="whenUtc">The timestamp describing when rehydration occurred.</param>
    public void Rehydrate(string? newPath, UtcTimestamp whenUtc)
    {
        var normalized = newPath is null ? StoragePath : NormalizeStoragePath(newPath);
        var pathChanged = !string.Equals(StoragePath, normalized, StringComparison.Ordinal);
        var wasMissing = IsMissing;

        StoragePath = normalized;
        IsMissing = false;

        if (!wasMissing && !pathChanged)
        {
            return;
        }

        RaiseDomainEvent(new FileSystemRehydrated(Id, Provider, normalized, wasMissing, whenUtc));
    }

    private static string NormalizeStoragePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Storage path cannot be null or whitespace.", nameof(value));
        }

        return value.Trim();
    }

    private static string? NormalizeOwnerSid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}

/// <summary>
/// Defines the supported storage providers for physical file content.
/// </summary>
public enum StorageProvider
{
    /// <summary>
    /// Local disk storage.
    /// </summary>
    Local = 0,

    /// <summary>
    /// Network storage (e.g., SMB or NFS shares).
    /// </summary>
    Network = 1,
}
