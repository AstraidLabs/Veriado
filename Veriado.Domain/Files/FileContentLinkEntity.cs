// VERIADO REFACTOR
using Veriado.Domain.Metadata;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.Files;

/// <summary>
/// Represents an immutable link to external file content stored outside of the domain aggregate.
/// </summary>
public sealed class FileContentLinkEntity
{
    // VERIADO REFACTOR
    private FileContentLinkEntity(
        Guid id,
        Guid fileSystemId,
        StorageProvider provider,
        StoragePath path,
        FileHash hash,
        ByteSize size,
        MimeType mime,
        bool isEncrypted,
        FileAttributesFlags attributes,
        ContentVersion version,
        UtcTimestamp linkedUtc)
    {
        Id = id;
        FileSystemId = fileSystemId;
        Provider = provider;
        Path = path;
        Hash = hash;
        Size = size;
        Mime = mime;
        IsEncrypted = isEncrypted;
        Attributes = attributes;
        Version = version;
        LinkedUtc = linkedUtc;
    }

    // VERIADO REFACTOR
    public Guid Id { get; }

    // VERIADO REFACTOR
    public Guid FileSystemId { get; }

    // VERIADO REFACTOR
    public StorageProvider Provider { get; }

    // VERIADO REFACTOR
    public StoragePath Path { get; }

    // VERIADO REFACTOR
    public FileHash Hash { get; }

    // VERIADO REFACTOR
    public ByteSize Size { get; }

    // VERIADO REFACTOR
    public MimeType Mime { get; }

    // VERIADO REFACTOR
    public bool IsEncrypted { get; }

    // VERIADO REFACTOR
    public FileAttributesFlags Attributes { get; }

    // VERIADO REFACTOR
    public ContentVersion Version { get; }

    // VERIADO REFACTOR
    public UtcTimestamp LinkedUtc { get; }

    // VERIADO REFACTOR
    public static FileContentLinkEntity CreateNew(
        Guid fileSystemId,
        StorageProvider provider,
        StoragePath path,
        FileHash hash,
        ByteSize size,
        MimeType mime,
        bool isEncrypted,
        FileAttributesFlags attributes,
        UtcTimestamp linkedUtc)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new FileContentLinkEntity(
            Guid.NewGuid(),
            fileSystemId,
            provider,
            path,
            hash,
            size,
            mime,
            isEncrypted,
            attributes,
            ContentVersion.Initial(),
            linkedUtc);
    }

    // VERIADO REFACTOR
    public FileContentLinkEntity Relink(
        Guid fileSystemId,
        StorageProvider provider,
        StoragePath path,
        FileHash hash,
        ByteSize size,
        MimeType mime,
        bool isEncrypted,
        FileAttributesFlags attributes,
        UtcTimestamp linkedUtc)
    {
        ArgumentNullException.ThrowIfNull(path);
        var nextVersion = hash == Hash ? Version : Version.Next();
        return new FileContentLinkEntity(
            Guid.NewGuid(),
            fileSystemId,
            provider,
            path,
            hash,
            size,
            mime,
            isEncrypted,
            attributes,
            nextVersion,
            linkedUtc);
    }
}
