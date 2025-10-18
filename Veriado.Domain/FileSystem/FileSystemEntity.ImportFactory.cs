using System;
using Veriado.Domain.Metadata;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.FileSystem;

public sealed partial class FileSystemEntity
{
    internal static FileSystemEntity CreateForImport(
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
        var entity = new FileSystemEntity(id)
        {
            Provider = provider,
            Path = path,
            Hash = hash,
            Size = size,
            Mime = mime,
            Attributes = attributes,
            OwnerSid = string.IsNullOrWhiteSpace(ownerSid) ? null : ownerSid.Trim(),
            IsEncrypted = isEncrypted,
            CreatedUtc = createdUtc,
            LastWriteUtc = lastWriteUtc,
            LastAccessUtc = lastAccessUtc,
            ContentVersion = contentVersion,
            IsMissing = isMissing,
            MissingSinceUtc = missingSinceUtc,
            LastLinkedUtc = lastLinkedUtc,
        };

        return entity;
    }
}
