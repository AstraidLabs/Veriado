using System;
using Veriado.Domain.Files.Events;
using Veriado.Domain.Metadata;
using Veriado.Domain.Search;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.Files;

public sealed partial class FileEntity
{
    internal static FileEntity CreateForImport(
        Guid id,
        FileName name,
        FileExtension extension,
        MimeType mime,
        string author,
        Guid fileSystemId,
        FileHash contentHash,
        ByteSize size,
        ContentVersion linkedContentVersion,
        int contentRevision,
        UtcTimestamp createdUtc,
        UtcTimestamp lastModifiedUtc,
        bool isReadOnly,
        FileSystemMetadata systemMetadata,
        FileDocumentValidityEntity? validity,
        SearchIndexState searchIndex,
        Fts5Policy ftsPolicy,
        string? title,
        bool emitCreationEvents = false)
    {
        var entity = new FileEntity(id)
        {
            Name = name,
            Extension = extension,
            Mime = mime,
            Author = NormalizeAuthor(author),
            FileSystemId = fileSystemId,
            ContentHash = contentHash,
            Size = size,
            LinkedContentVersion = linkedContentVersion,
            ContentRevision = contentRevision,
            CreatedUtc = createdUtc,
            LastModifiedUtc = lastModifiedUtc,
            IsReadOnly = isReadOnly,
            SystemMetadata = systemMetadata,
            Validity = validity,
            SearchIndex = searchIndex,
            FtsPolicy = ftsPolicy,
            Title = NormalizeOptionalText(title),
        };

        if (emitCreationEvents)
        {
            entity.RaiseDomainEvent(new FileCreated(
                entity.Id,
                entity.Name,
                entity.Extension,
                entity.Mime,
                entity.Author,
                entity.Size,
                entity.ContentHash,
                createdUtc));
        }

        entity.RaiseDomainEvent(new FileContentLinked(
            entity.Id,
            entity.FileSystemId,
            entity.LinkedContentVersion,
            entity.ContentHash,
            entity.Size,
            entity.Mime,
            lastModifiedUtc));

        return entity;
    }
}
