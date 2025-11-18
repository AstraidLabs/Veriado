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
        var entity = new FileSystemEntity(id)
        {
            Provider = provider,
            RelativePath = relativePath,
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
            IsMissing = isMissing || physicalState == FilePhysicalState.Missing,
            MissingSinceUtc = (isMissing || physicalState == FilePhysicalState.Missing) ? missingSinceUtc : null,
            LastLinkedUtc = lastLinkedUtc,
            CurrentFilePath = currentFilePath ?? string.Empty,
            OriginalFilePath = string.IsNullOrWhiteSpace(originalFilePath)
                ? currentFilePath ?? string.Empty
                : originalFilePath.Trim(),
            PhysicalState = isMissing
                ? FilePhysicalState.Missing
                : physicalState == FilePhysicalState.Unknown
                    ? FilePhysicalState.Healthy
                    : physicalState,
        };

        return entity;
    }
}
