using System;
using Veriado.Domain.Metadata;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.FileSystem;

public sealed partial class FileSystemEntity
{
    internal static FileSystemEntity CreateForImport(
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
            physicalState,
            raiseInitialEvents: true);
    }
}
