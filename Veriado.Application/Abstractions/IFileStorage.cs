using System.IO;

namespace Veriado.Appl.Abstractions;

/// <summary>
/// Provides access to the underlying storage provider that hosts binary file content.
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// Persists the provided content stream and returns the resulting metadata snapshot.
    /// </summary>
    /// <param name="content">The content stream to persist.</param>
    /// <param name="options">The storage options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The storage metadata describing the saved content.</returns>
    Task<StorageResult> SaveAsync(Stream content, StorageSaveOptions? options, CancellationToken cancellationToken);

    /// <summary>
    /// Moves existing stored content to a different logical path.
    /// </summary>
    /// <param name="from">The original storage path.</param>
    /// <param name="to">The desired target path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The normalized path pointing to the moved content.</returns>
    Task<StoragePath> MoveAsync(StoragePath from, StoragePath to, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves the latest metadata snapshot for the specified storage path.
    /// </summary>
    /// <param name="path">The storage path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The metadata describing the stored content.</returns>
    Task<FileStat> StatAsync(StoragePath path, CancellationToken cancellationToken);
}

/// <summary>
/// Represents the outcome of a storage write operation.
/// </summary>
/// <param name="Provider">The storage provider that hosts the content.</param>
/// <param name="Path">The normalized storage path.</param>
/// <param name="Hash">The SHA-256 content hash.</param>
/// <param name="Size">The size of the stored content.</param>
/// <param name="Mime">The MIME type detected for the content.</param>
/// <param name="Attributes">The observed file attributes.</param>
/// <param name="OwnerSid">The optional owner security identifier.</param>
/// <param name="IsEncrypted">Indicates whether the content is encrypted at rest.</param>
/// <param name="CreatedUtc">The creation timestamp.</param>
/// <param name="LastWriteUtc">The last write timestamp.</param>
/// <param name="LastAccessUtc">The last access timestamp.</param>
public readonly record struct StorageResult(
    StorageProvider Provider,
    StoragePath Path,
    FileHash Hash,
    ByteSize Size,
    MimeType Mime,
    FileAttributesFlags Attributes,
    string? OwnerSid,
    bool IsEncrypted,
    UtcTimestamp CreatedUtc,
    UtcTimestamp LastWriteUtc,
    UtcTimestamp LastAccessUtc)
{
    /// <summary>
    /// Converts the storage result to a <see cref="FileStat"/> snapshot.
    /// </summary>
    /// <returns>The corresponding file stat.</returns>
    public FileStat ToFileStat()
        => new(
            Provider,
            Path,
            Hash,
            Size,
            Mime,
            Attributes,
            OwnerSid,
            IsEncrypted,
            CreatedUtc,
            LastWriteUtc,
            LastAccessUtc);
}

/// <summary>
/// Provides optional parameters for saving content to storage.
/// </summary>
/// <param name="Extension">The preferred file extension including the leading dot.</param>
/// <param name="Mime">The MIME type of the content.</param>
/// <param name="OriginalFileName">The original file name used to infer an extension when needed.</param>
public sealed record class StorageSaveOptions(string? Extension = null, string? Mime = null, string? OriginalFileName = null);

/// <summary>
/// Represents metadata describing stored content.
/// </summary>
/// <param name="Provider">The storage provider that hosts the content.</param>
/// <param name="Path">The normalized storage path.</param>
/// <param name="Hash">The SHA-256 content hash.</param>
/// <param name="Size">The size of the stored content.</param>
/// <param name="Mime">The MIME type detected for the content.</param>
/// <param name="Attributes">The observed file attributes.</param>
/// <param name="OwnerSid">The optional owner security identifier.</param>
/// <param name="IsEncrypted">Indicates whether the content is encrypted at rest.</param>
/// <param name="CreatedUtc">The creation timestamp.</param>
/// <param name="LastWriteUtc">The last write timestamp.</param>
/// <param name="LastAccessUtc">The last access timestamp.</param>
public readonly record struct FileStat(
    StorageProvider Provider,
    StoragePath Path,
    FileHash Hash,
    ByteSize Size,
    MimeType Mime,
    FileAttributesFlags Attributes,
    string? OwnerSid,
    bool IsEncrypted,
    UtcTimestamp CreatedUtc,
    UtcTimestamp LastWriteUtc,
    UtcTimestamp LastAccessUtc)
{
    /// <summary>
    /// Converts the stat snapshot to a <see cref="StorageResult"/> for convenience.
    /// </summary>
    /// <returns>The corresponding storage result.</returns>
    public StorageResult ToStorageResult()
        => new(
            Provider,
            Path,
            Hash,
            Size,
            Mime,
            Attributes,
            OwnerSid,
            IsEncrypted,
            CreatedUtc,
            LastWriteUtc,
            LastAccessUtc);
}
