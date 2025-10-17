using Veriado.Domain.FileSystem.Events;
using Veriado.Domain.Metadata;
using Veriado.Domain.Primitives;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.FileSystem;

/// <summary>
/// Represents the metadata snapshot of a physical file stored externally.
/// </summary>
public sealed class FileSystemEntity : AggregateRoot
{
    private FileSystemEntity(Guid id)
        : base(id)
    {
    }

    private FileSystemEntity(
        Guid id,
        StorageProvider provider,
        StoragePath path,
        FileHash hash,
        ByteSize size,
        MimeType mime,
        FileAttributesFlags attributes,
        string? ownerSid,
        bool isEncrypted,
        UtcTimestamp createdUtc,
        UtcTimestamp lastWriteUtc,
        UtcTimestamp lastAccessUtc,
        ContentVersion contentVersion,
        bool isMissing,
        UtcTimestamp? missingSinceUtc,
        UtcTimestamp? lastLinkedUtc)
        : base(id)
    {
        Provider = provider;
        Path = path;
        Hash = hash;
        Size = size;
        Mime = mime;
        Attributes = attributes;
        OwnerSid = ownerSid;
        IsEncrypted = isEncrypted;
        CreatedUtc = createdUtc;
        LastWriteUtc = lastWriteUtc;
        LastAccessUtc = lastAccessUtc;
        ContentVersion = contentVersion;
        IsMissing = isMissing;
        MissingSinceUtc = missingSinceUtc;
        LastLinkedUtc = lastLinkedUtc;
    }

    /// <summary>
    /// Gets the storage provider that contains the physical content.
    /// </summary>
    public StorageProvider Provider { get; private set; }

    /// <summary>
    /// Gets the normalized path pointing to the stored content.
    /// </summary>
    public StoragePath Path { get; private set; } = null!;

    /// <summary>
    /// Gets the SHA-256 hash of the stored content.
    /// </summary>
    public FileHash Hash { get; private set; }

    /// <summary>
    /// Gets the size of the stored content in bytes.
    /// </summary>
    public ByteSize Size { get; private set; }

    /// <summary>
    /// Gets the MIME type associated with the stored content.
    /// </summary>
    public MimeType Mime { get; private set; }

    /// <summary>
    /// Gets the file attribute flags observed when the entity was last refreshed.
    /// </summary>
    public FileAttributesFlags Attributes { get; private set; }

    /// <summary>
    /// Gets the owner security identifier if available.
    /// </summary>
    public string? OwnerSid { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the content is encrypted at rest.
    /// </summary>
    public bool IsEncrypted { get; private set; }

    /// <summary>
    /// Gets the creation timestamp of the file in UTC.
    /// </summary>
    public UtcTimestamp CreatedUtc { get; private set; }

    /// <summary>
    /// Gets the last write timestamp of the file in UTC.
    /// </summary>
    public UtcTimestamp LastWriteUtc { get; private set; }

    /// <summary>
    /// Gets the last access timestamp of the file in UTC.
    /// </summary>
    public UtcTimestamp LastAccessUtc { get; private set; }

    /// <summary>
    /// Gets the version of the physical content referenced by the entity.
    /// </summary>
    public ContentVersion ContentVersion { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the content is currently missing.
    /// </summary>
    public bool IsMissing { get; private set; }

    /// <summary>
    /// Gets the timestamp when the content was first observed as missing, if applicable.
    /// </summary>
    public UtcTimestamp? MissingSinceUtc { get; private set; }

    /// <summary>
    /// Gets the timestamp when the content was last linked to a logical file, if known.
    /// </summary>
    public UtcTimestamp? LastLinkedUtc { get; private set; }

    /// <summary>
    /// Creates a new file system entity from the supplied metadata.
    /// </summary>
    /// <param name="provider">The storage provider hosting the content.</param>
    /// <param name="path">The storage path referencing the content.</param>
    /// <param name="hash">The hash of the content.</param>
    /// <param name="size">The size of the content.</param>
    /// <param name="mime">The MIME type of the content.</param>
    /// <param name="attributes">The file attributes snapshot.</param>
    /// <param name="ownerSid">The owner security identifier, if known.</param>
    /// <param name="isEncrypted">Indicates whether the content is encrypted.</param>
    /// <param name="createdUtc">The creation timestamp.</param>
    /// <param name="lastWriteUtc">The last write timestamp.</param>
    /// <param name="lastAccessUtc">The last access timestamp.</param>
    /// <param name="lastLinkedUtc">Optional timestamp describing the last logical linkage.</param>
    /// <returns>The created aggregate.</returns>
    public static FileSystemEntity CreateNew(
        StorageProvider provider,
        StoragePath path,
        FileHash hash,
        ByteSize size,
        MimeType mime,
        FileAttributesFlags attributes,
        string? ownerSid,
        bool isEncrypted,
        UtcTimestamp createdUtc,
        UtcTimestamp lastWriteUtc,
        UtcTimestamp lastAccessUtc,
        UtcTimestamp? lastLinkedUtc = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        var normalizedOwner = NormalizeOwnerSid(ownerSid);
        var entity = new FileSystemEntity(
            Guid.NewGuid(),
            provider,
            path,
            hash,
            size,
            mime,
            attributes,
            normalizedOwner,
            isEncrypted,
            createdUtc,
            lastWriteUtc,
            lastAccessUtc,
            ContentVersion.Initial,
            isMissing: false,
            missingSinceUtc: null,
            lastLinkedUtc);

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

        if (attributes != FileAttributesFlags.None)
        {
            entity.RaiseDomainEvent(new FileSystemAttributesChanged(entity.Id, attributes, createdUtc));
        }

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

        return entity;
    }

    /// <summary>
    /// Replaces the stored content with a new blob and refreshes metadata.
    /// </summary>
    /// <param name="path">The storage path referencing the content.</param>
    /// <param name="hash">The new content hash.</param>
    /// <param name="size">The new content size.</param>
    /// <param name="mime">The new MIME type.</param>
    /// <param name="isEncrypted">Indicates whether the content is encrypted.</param>
    /// <param name="whenUtc">The timestamp when the replacement occurred.</param>
    public void ReplaceContent(
        StoragePath path,
        FileHash hash,
        ByteSize size,
        MimeType mime,
        bool isEncrypted,
        UtcTimestamp whenUtc)
    {
        ArgumentNullException.ThrowIfNull(path);

        var pathChanged = !Path.Equals(path);
        var hashChanged = Hash != hash;
        var sizeChanged = Size != size;
        var mimeChanged = Mime != mime;
        var encryptionChanged = IsEncrypted != isEncrypted;
        var missingChanged = IsMissing;

        if (!pathChanged && !hashChanged && !sizeChanged && !mimeChanged && !encryptionChanged && !missingChanged)
        {
            return;
        }

        Path = path;
        if (hashChanged)
        {
            Hash = hash;
            ContentVersion = ContentVersion.Next();
        }
        else
        {
            Hash = hash;
        }

        Size = size;
        Mime = mime;
        IsEncrypted = isEncrypted;
        IsMissing = false;
        MissingSinceUtc = null;
        LastWriteUtc = whenUtc;

        RaiseDomainEvent(new FileSystemContentChanged(Id, Provider, Path, Hash, Size, Mime, ContentVersion, IsEncrypted, whenUtc));
    }

    /// <summary>
    /// Moves the stored content to a different path within the same provider.
    /// </summary>
    /// <param name="newPath">The new storage path.</param>
    /// <param name="whenUtc">The timestamp when the move occurred.</param>
    public void MoveTo(StoragePath newPath, UtcTimestamp whenUtc)
    {
        ArgumentNullException.ThrowIfNull(newPath);

        if (Path.Equals(newPath))
        {
            return;
        }

        var previous = Path;
        Path = newPath;

        RaiseDomainEvent(new FileSystemMoved(Id, Provider, previous, newPath, whenUtc));
    }

    /// <summary>
    /// Updates the stored attribute snapshot.
    /// </summary>
    /// <param name="attributes">The new attribute flags.</param>
    /// <param name="whenUtc">The timestamp when the change was observed.</param>
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
    /// Updates the owner security identifier associated with the file.
    /// </summary>
    /// <param name="ownerSid">The new owner security identifier.</param>
    /// <param name="whenUtc">The timestamp when the change was observed.</param>
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
    /// Updates the tracked timestamps.
    /// </summary>
    /// <param name="createdUtc">Optional creation timestamp.</param>
    /// <param name="lastWriteUtc">Optional last write timestamp.</param>
    /// <param name="lastAccessUtc">Optional last access timestamp.</param>
    /// <param name="whenUtc">The timestamp when the change was observed.</param>
    public void UpdateTimestamps(
        UtcTimestamp? createdUtc,
        UtcTimestamp? lastWriteUtc,
        UtcTimestamp? lastAccessUtc,
        UtcTimestamp whenUtc)
    {
        var changed = false;

        if (createdUtc.HasValue && createdUtc.Value != CreatedUtc)
        {
            CreatedUtc = createdUtc.Value;
            changed = true;
        }

        if (lastWriteUtc.HasValue && lastWriteUtc.Value != LastWriteUtc)
        {
            LastWriteUtc = lastWriteUtc.Value;
            changed = true;
        }

        if (lastAccessUtc.HasValue && lastAccessUtc.Value != LastAccessUtc)
        {
            LastAccessUtc = lastAccessUtc.Value;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        RaiseDomainEvent(new FileSystemTimestampsUpdated(Id, CreatedUtc, LastWriteUtc, LastAccessUtc, whenUtc));
    }

    /// <summary>
    /// Marks the entity as missing from the underlying storage.
    /// </summary>
    /// <param name="whenUtc">The timestamp when the missing state was detected.</param>
    public void MarkMissing(UtcTimestamp whenUtc)
    {
        if (IsMissing)
        {
            return;
        }

        IsMissing = true;
        MissingSinceUtc = whenUtc;

        RaiseDomainEvent(new FileSystemMissingDetected(Id, Path, whenUtc, MissingSinceUtc));
    }

    /// <summary>
    /// Clears the missing state and optionally updates the storage path.
    /// </summary>
    /// <param name="newPath">An optional new storage path.</param>
    /// <param name="whenUtc">The timestamp when rehydration occurred.</param>
    public void Rehydrate(StoragePath? newPath, UtcTimestamp whenUtc)
    {
        var normalized = newPath ?? Path;
        var pathChanged = !Path.Equals(normalized);
        var wasMissing = IsMissing;
        var previousMissingSince = MissingSinceUtc;

        if (!pathChanged && !wasMissing)
        {
            return;
        }

        Path = normalized;
        IsMissing = false;
        MissingSinceUtc = null;
        LastLinkedUtc = whenUtc;

        RaiseDomainEvent(new FileSystemRehydrated(Id, Provider, normalized, wasMissing, previousMissingSince, whenUtc));
    }

    /// <summary>
    /// Rehydrates the entity from persisted state without raising events.
    /// </summary>
    /// <param name="state">The persisted state to restore.</param>
    /// <returns>The restored aggregate.</returns>
    public static FileSystemEntity Rehydrate(
        Guid id,
        StorageProvider provider,
        StoragePath path,
        FileHash hash,
        ByteSize size,
        MimeType mime,
        FileAttributesFlags attributes,
        string? ownerSid,
        bool isEncrypted,
        UtcTimestamp createdUtc,
        UtcTimestamp lastWriteUtc,
        UtcTimestamp lastAccessUtc,
        ContentVersion contentVersion,
        bool isMissing,
        UtcTimestamp? missingSinceUtc,
        UtcTimestamp? lastLinkedUtc)
    {
        ArgumentNullException.ThrowIfNull(path);

        return new FileSystemEntity(
            id,
            provider,
            path,
            hash,
            size,
            mime,
            attributes,
            NormalizeOwnerSid(ownerSid),
            isEncrypted,
            createdUtc,
            lastWriteUtc,
            lastAccessUtc,
            contentVersion,
            isMissing,
            missingSinceUtc,
            lastLinkedUtc);
    }

    private static string? NormalizeOwnerSid(string? ownerSid)
    {
        if (string.IsNullOrWhiteSpace(ownerSid))
        {
            return null;
        }

        return ownerSid.Trim();
    }
}
