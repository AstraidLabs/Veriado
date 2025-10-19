using System;
using Veriado.Application.Import;
using Veriado.Domain.FileSystem;
using Veriado.Domain.Files;
using Veriado.Domain.Metadata;
using Veriado.Domain.Search;
using Veriado.Domain.ValueObjects;

namespace Veriado.Infrastructure.Import;

internal static class ImportMapping
{
    public static MappedImport MapToAggregate(ImportItem item, Guid? existingFileSystemId = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.Metadata is not ImportMetadata metadata)
        {
            throw new InvalidOperationException("Import metadata payload must be of type ImportMetadata.");
        }

        var fileId = item.FileId == Guid.Empty ? Guid.NewGuid() : item.FileId;
        var fileSystemId = existingFileSystemId ?? metadata.FileSystemId ?? Guid.NewGuid();

        var name = FileName.From(item.Name);
        var extension = FileExtension.From(item.Extension ?? string.Empty);
        var mime = MimeType.From(item.Mime);
        var hash = FileHash.From(item.Hash);
        var size = ByteSize.From(item.SizeBytes);
        var contentVersion = ContentVersion.From(metadata.LinkedContentVersion);
        var createdUtc = UtcTimestamp.From(item.CreatedUtc ?? DateTimeOffset.UtcNow);
        var modifiedUtc = UtcTimestamp.From(item.ModifiedUtc ?? item.CreatedUtc ?? DateTimeOffset.UtcNow);
        var systemMetadata = BuildSystemMetadata(metadata.FileSystem, createdUtc, modifiedUtc);
        var validity = BuildValidity(metadata.Validity);
        var searchIndex = BuildSearchIndex(metadata.Search);
        var ftsPolicy = BuildFtsPolicy(metadata.FtsPolicy);

        var provider = ParseProvider(item.StorageProvider);
        var path = StoragePath.From(item.StoragePath);

        var fsCreated = metadata.FileSystem?.CreatedUtc ?? item.CreatedUtc ?? DateTimeOffset.UtcNow;
        var fsWrite = metadata.FileSystem?.LastWriteUtc ?? item.ModifiedUtc ?? item.CreatedUtc ?? DateTimeOffset.UtcNow;
        var fsAccess = metadata.FileSystem?.LastAccessUtc ?? fsWrite;

        var fileSystem = FileSystemEntity.CreateForImport(
            fileSystemId,
            provider,
            path,
            hash,
            size,
            mime,
            systemMetadata.Attributes,
            metadata.FileSystem?.OwnerSid,
            metadata.FileSystem?.IsEncrypted ?? false,
            UtcTimestamp.From(fsCreated),
            UtcTimestamp.From(fsWrite),
            UtcTimestamp.From(fsAccess),
            contentVersion,
            isMissing: false,
            missingSinceUtc: null,
            lastLinkedUtc: modifiedUtc);

        var file = FileEntity.CreateForImport(
            fileId,
            name,
            extension,
            mime,
            metadata.Author,
            fileSystem.Id,
            provider.ToString(),
            path.Value,
            hash,
            size,
            contentVersion,
            metadata.Version,
            createdUtc,
            modifiedUtc,
            metadata.IsReadOnly,
            systemMetadata,
            validity,
            searchIndex,
            ftsPolicy,
            metadata.Title);

        return new MappedImport(file, fileSystem, metadata.Version, metadata.Search);
    }

    private static FileSystemMetadata BuildSystemMetadata(
        ImportFileSystemMetadata? metadata,
        UtcTimestamp createdUtc,
        UtcTimestamp modifiedUtc)
    {
        if (metadata is null)
        {
            return new FileSystemMetadata(
                FileAttributesFlags.Normal,
                createdUtc,
                modifiedUtc,
                modifiedUtc,
                ownerSid: null,
                hardLinkCount: null,
                alternateDataStreamCount: null);
        }

        var created = UtcTimestamp.From(metadata.CreatedUtc ?? createdUtc.Value);
        var write = UtcTimestamp.From(metadata.LastWriteUtc ?? modifiedUtc.Value);
        var access = UtcTimestamp.From(metadata.LastAccessUtc ?? modifiedUtc.Value);

        return new FileSystemMetadata(
            (FileAttributesFlags)metadata.Attributes,
            created,
            write,
            access,
            metadata.OwnerSid,
            metadata.HardLinkCount,
            metadata.AlternateDataStreamCount);
    }

    private static FileDocumentValidityEntity? BuildValidity(ImportValidity? validity)
    {
        if (validity is null)
        {
            return null;
        }

        return new FileDocumentValidityEntity(
            UtcTimestamp.From(validity.IssuedAt),
            UtcTimestamp.From(validity.ValidUntil),
            validity.HasPhysicalCopy,
            validity.HasElectronicCopy);
    }

    private static SearchIndexState BuildSearchIndex(ImportSearchMetadata? metadata)
    {
        if (metadata is null)
        {
            return new SearchIndexState(schemaVersion: 1, isStale: true);
        }

        return new SearchIndexState(
            metadata.SchemaVersion <= 0 ? 1 : metadata.SchemaVersion,
            metadata.IsStale,
            metadata.IndexedUtc,
            metadata.IndexedContentHash,
            metadata.IndexedTitle,
            metadata.AnalyzerVersion,
            metadata.TokenHash);
    }

    private static Fts5Policy BuildFtsPolicy(ImportFtsPolicy? policy)
    {
        if (policy is null)
        {
            return Fts5Policy.Default;
        }

        return new Fts5Policy(policy.RemoveDiacritics, policy.Tokenizer, policy.TokenChars);
    }

    private static StorageProvider ParseProvider(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return StorageProvider.Local;
        }

        if (Enum.TryParse<StorageProvider>(provider, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return StorageProvider.Local;
    }
}

internal sealed record MappedImport(
    FileEntity File,
    FileSystemEntity FileSystem,
    int Version,
    ImportSearchMetadata? SearchMetadata);
