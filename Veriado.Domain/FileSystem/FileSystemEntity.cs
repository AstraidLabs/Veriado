using System;
using System.IO;
using Veriado.Domain.FileSystem.Events;
using Veriado.Domain.Metadata;
using Veriado.Domain.Primitives;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.FileSystem;

/// <summary>
/// Represents the metadata snapshot of a physical file stored externally.
/// </summary>
public sealed partial class FileSystemEntity : AggregateRoot
{
    private FileSystemEntity(Guid id)
        : base(id)
    {
    }

    /// <summary>
    /// Gets the storage provider that contains the physical content.
    /// </summary>
    public StorageProvider Provider { get; private set; }

    /// <summary>
    /// Gets the normalized relative path pointing to the stored content under the storage root.
    /// </summary>
    public RelativeFilePath RelativePath { get; private set; } = null!;

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
    /// <remarks>
    /// Maintained for legacy compatibility and kept in sync with <see cref="PhysicalState"/>.
    /// </remarks>
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
    /// Gets the currently observed physical path of the file on disk.
    /// </summary>
    public string CurrentFilePath { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the original physical path observed during import.
    /// </summary>
    public string OriginalFilePath { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the current physical health state of the file.
    /// </summary>
    public FilePhysicalState PhysicalState { get; private set; }

    /// <summary>
    /// Creates a new file system entity from the supplied metadata.
    /// </summary>
    /// <param name="provider">The storage provider hosting the content.</param>
    /// <param name="relativePath">The relative storage path referencing the content.</param>
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
        RelativeFilePath relativePath,
        FileHash hash,
        ByteSize size,
        MimeType mime,
        FileAttributesFlags attributes,
        string? ownerSid,
        bool isEncrypted,
        UtcTimestamp createdUtc,
        UtcTimestamp lastWriteUtc,
        UtcTimestamp lastAccessUtc,
        UtcTimestamp? lastLinkedUtc = null,
        string? currentFilePath = null,
        string? originalFilePath = null,
        FilePhysicalState physicalState = FilePhysicalState.Healthy)
    {
        return CreateCore(
            Guid.NewGuid(),
            provider,
            relativePath,
            hash,
            size,
            mime,
            attributes,
            ownerSid,
            isEncrypted,
            createdUtc,
            lastWriteUtc,
            lastAccessUtc,
            ContentVersion.Initial,
            isMissing: false,
            missingSinceUtc: null,
            lastLinkedUtc,
            currentFilePath,
            originalFilePath,
            physicalState,
            raiseInitialEvents: true);
    }

    private static FileSystemEntity CreateCore(
        Guid id,
        StorageProvider provider,
        RelativeFilePath relativePath,
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
        UtcTimestamp? lastLinkedUtc,
        string? currentFilePath,
        string? originalFilePath,
        FilePhysicalState physicalState,
        bool raiseInitialEvents)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        var entity = new FileSystemEntity(id);
        entity.Initialize(
            provider,
            relativePath,
            hash,
            size,
            mime,
            attributes,
            ownerSid,
            isEncrypted,
            createdUtc,
            lastWriteUtc,
            lastAccessUtc,
            contentVersion,
            isMissing,
            missingSinceUtc,
            lastLinkedUtc,
            currentFilePath,
            originalFilePath,
            physicalState);

        entity.ReconcilePhysicalState();

        var normalizedOwner = entity.OwnerSid;

        if (raiseInitialEvents)
        {
            entity.RaiseDomainEvent(new FileSystemContentChanged(
                entity.Id,
                provider,
                relativePath,
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
        }

        return entity;
    }

    private void Initialize(
        StorageProvider provider,
        RelativeFilePath relativePath,
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
        UtcTimestamp? lastLinkedUtc,
        string? currentFilePath,
        string? originalFilePath,
        FilePhysicalState physicalState)
    {
        Provider = provider;
        RelativePath = relativePath;
        Hash = hash;
        Size = size;
        Mime = mime;
        Attributes = attributes;
        OwnerSid = NormalizeOwnerSid(ownerSid);
        IsEncrypted = isEncrypted;
        CreatedUtc = createdUtc;
        LastWriteUtc = lastWriteUtc;
        LastAccessUtc = lastAccessUtc;
        LastLinkedUtc = lastLinkedUtc;
        ContentVersion = contentVersion;

        ValidateAndSetPhysicalPaths(currentFilePath, originalFilePath);

        var normalizedState = NormalizePhysicalState(physicalState, isMissing);
        SetPhysicalState(normalizedState, missingSinceUtc);
    }

    /// <summary>
    /// Replaces the stored content with a new blob and refreshes metadata.
    /// </summary>
    /// <param name="relativePath">The relative storage path referencing the content.</param>
    /// <param name="hash">The new content hash.</param>
    /// <param name="size">The new content size.</param>
    /// <param name="mime">The new MIME type.</param>
    /// <param name="isEncrypted">Indicates whether the content is encrypted.</param>
    /// <param name="whenUtc">The timestamp when the replacement occurred.</param>
    public void ReplaceContent(
        RelativeFilePath relativePath,
        FileHash hash,
        ByteSize size,
        MimeType mime,
        bool isEncrypted,
        UtcTimestamp whenUtc)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        var pathChanged = !RelativePath.Equals(relativePath);
        var hashChanged = Hash != hash;
        var sizeChanged = Size != size;
        var mimeChanged = Mime != mime;
        var encryptionChanged = IsEncrypted != isEncrypted;
        var missingChanged = IsMissing;

        if (!pathChanged && !hashChanged && !sizeChanged && !mimeChanged && !encryptionChanged && !missingChanged)
        {
            return;
        }

        RelativePath = relativePath;
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
        MarkContentChanged();
        LastWriteUtc = whenUtc;
        LastAccessUtc = whenUtc;
        LastLinkedUtc = whenUtc;

        RaiseDomainEvent(new FileSystemContentChanged(Id, Provider, RelativePath, Hash, Size, Mime, ContentVersion, IsEncrypted, whenUtc));
    }

    /// <summary>
    /// Moves the stored content to a different path within the same provider.
    /// </summary>
    /// <param name="newPath">The new relative storage path.</param>
    /// <param name="whenUtc">The timestamp when the move occurred.</param>
    /// <remarks>
    /// The missing state (<see cref="IsMissing"/>) is not altered by this operation; it only updates the tracked relative
    /// path and emits a <see cref="FileSystemMoved"/> event so that infrastructure can adjust absolute paths derived from
    /// the storage root.
    /// </remarks>
    public void MoveTo(RelativeFilePath newPath, UtcTimestamp whenUtc)
    {
        ArgumentNullException.ThrowIfNull(newPath);

        if (RelativePath.Equals(newPath))
        {
            return;
        }

        var previous = RelativePath;
        RelativePath = newPath;

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
    /// Clears the missing state and optionally updates the storage path.
    /// </summary>
    /// <param name="newPath">An optional new relative storage path.</param>
    /// <param name="whenUtc">The timestamp when rehydration occurred.</param>
    public void Rehydrate(RelativeFilePath? newPath, UtcTimestamp whenUtc)
    {
        var normalized = newPath ?? RelativePath;
        var pathChanged = !RelativePath.Equals(normalized);
        var wasMissing = IsMissing;
        var previousMissingSince = MissingSinceUtc;

        if (!pathChanged && !wasMissing)
        {
            return;
        }

        RelativePath = normalized;
        MarkHealthy();
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
        RelativeFilePath relativePath,
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
        UtcTimestamp? lastLinkedUtc,
        string? currentFilePath = null,
        string? originalFilePath = null,
        FilePhysicalState physicalState = FilePhysicalState.Unknown)
    {
        return CreateCore(
            id,
            provider,
            relativePath,
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
            lastLinkedUtc,
            currentFilePath,
            originalFilePath,
            physicalState,
            raiseInitialEvents: false);
    }

    /// <summary>
    /// Ensures legacy missing flags and the physical state remain aligned before persistence.
    /// </summary>
    public void ReconcilePhysicalState()
    {
        if (PhysicalState == FilePhysicalState.Missing || IsMissing)
        {
            SetPhysicalState(FilePhysicalState.Missing, MissingSinceUtc ?? UtcTimestamp.From(DateTime.UtcNow));
        }
        else
        {
            SetPhysicalState(NormalizePhysicalState(PhysicalState, isMissing: false));
        }
    }

    private static string? NormalizeOwnerSid(string? ownerSid)
    {
        if (string.IsNullOrWhiteSpace(ownerSid))
        {
            return null;
        }

        return ownerSid.Trim();
    }

    private void ValidateAndSetPhysicalPaths(string? currentFilePath, string? originalFilePath)
    {
        var normalizedOriginal = NormalizeOptionalPhysicalPath(originalFilePath);
        var normalizedCurrent = NormalizeOptionalPhysicalPath(currentFilePath);

        if (!string.IsNullOrEmpty(normalizedOriginal))
        {
            OriginalFilePath = normalizedOriginal!;
        }

        if (!string.IsNullOrEmpty(normalizedCurrent))
        {
            CurrentFilePath = normalizedCurrent!;
            if (string.IsNullOrWhiteSpace(OriginalFilePath))
            {
                OriginalFilePath = normalizedCurrent!;
            }
        }
        else if (!string.IsNullOrWhiteSpace(OriginalFilePath))
        {
            CurrentFilePath = OriginalFilePath;
        }
        else
        {
            CurrentFilePath = string.Empty;
            OriginalFilePath = string.Empty;
        }
    }

    private static string? NormalizeOptionalPhysicalPath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        var trimmed = rawPath.Trim();
        if (trimmed.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Physical path cannot contain parent directory traversal.", nameof(rawPath));
        }

        if (!Path.IsPathRooted(trimmed))
        {
            throw new ArgumentException("Physical path must be rooted under a validated storage root.", nameof(rawPath));
        }

        var fullPath = Path.GetFullPath(trimmed);
        if (fullPath.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Physical path cannot contain parent directory traversal.", nameof(rawPath));
        }

        return fullPath;
    }

    private void SetPhysicalState(FilePhysicalState newState, UtcTimestamp? missingDetectedAt = null)
    {
        PhysicalState = newState;

        if (newState == FilePhysicalState.Missing)
        {
            if (!IsMissing)
            {
                MissingSinceUtc = missingDetectedAt;
            }
            else if (MissingSinceUtc is null)
            {
                MissingSinceUtc = missingDetectedAt;
            }

            IsMissing = true;
        }
        else
        {
            IsMissing = false;
            MissingSinceUtc = null;
        }
    }

    private static FilePhysicalState NormalizePhysicalState(FilePhysicalState physicalState, bool isMissing)
    {
        if (isMissing)
        {
            return FilePhysicalState.Missing;
        }

        return physicalState == FilePhysicalState.Unknown
            ? FilePhysicalState.Healthy
            : physicalState;
    }

    public void UpdatePath(string newPath)
    {
        var normalized = NormalizeOptionalPhysicalPath(newPath)
            ?? throw new ArgumentException("Physical path must be provided.", nameof(newPath));

        var pathChanged = !string.Equals(CurrentFilePath, normalized, StringComparison.Ordinal);

        ValidateAndSetPhysicalPaths(normalized, OriginalFilePath);

        if (!pathChanged)
        {
            return;
        }

        if (PhysicalState != FilePhysicalState.Missing)
        {
            MarkMovedOrRenamed();
        }
    }

    public void MarkMissing(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var whenUtc = UtcTimestamp.From(clock.UtcNow);
        if (PhysicalState != FilePhysicalState.Missing)
        {
            SetPhysicalState(FilePhysicalState.Missing, whenUtc);

            RaiseDomainEvent(new FileSystemMissingDetected(Id, RelativePath, whenUtc, MissingSinceUtc));
        }
    }

    public void MarkHealthy()
    {
        if (PhysicalState == FilePhysicalState.Healthy && !IsMissing)
        {
            return;
        }

        SetPhysicalState(FilePhysicalState.Healthy);
    }

    public void MarkContentChanged()
    {
        SetPhysicalState(FilePhysicalState.ContentChanged);
    }

    public void MarkMovedOrRenamed()
    {
        SetPhysicalState(FilePhysicalState.MovedOrRenamed);
    }

    public void MarkMovedOrRenamed(string newPath)
    {
        var normalized = NormalizeOptionalPhysicalPath(newPath)
            ?? throw new ArgumentException("Physical path must be provided.", nameof(newPath));

        var pathChanged = !string.Equals(CurrentFilePath, normalized, StringComparison.Ordinal);

        ValidateAndSetPhysicalPaths(normalized, OriginalFilePath);

        if (pathChanged || PhysicalState != FilePhysicalState.MovedOrRenamed || IsMissing)
        {
            MarkMovedOrRenamed();
        }
    }
}
