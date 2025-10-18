using System;
using Veriado.Domain.FileSystem.Events;
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
        UtcTimestamp? lastLinkedUtc,
        bool emitContentEvents = true)
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

        if (emitContentEvents)
        {
            entity.RaiseDomainEvent(new FileSystemContentChanged(
                entity.Id,
                provider,
                path,
                hash,
                size,
                mime,
                contentVersion,
                isEncrypted,
                lastLinkedUtc ?? createdUtc));

            entity.RaiseDomainEvent(new FileSystemTimestampsUpdated(
                entity.Id,
                createdUtc,
                lastWriteUtc,
                lastAccessUtc,
                lastLinkedUtc ?? createdUtc));

            if (attributes != FileAttributesFlags.None)
            {
                entity.RaiseDomainEvent(new FileSystemAttributesChanged(entity.Id, attributes, createdUtc));
            }

            if (!string.IsNullOrWhiteSpace(entity.OwnerSid))
            {
                entity.RaiseDomainEvent(new FileSystemOwnerChanged(entity.Id, entity.OwnerSid, createdUtc));
            }
        }

        return entity;
    }
}
