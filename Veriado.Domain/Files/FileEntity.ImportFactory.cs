using System;
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
        string? title)
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

        return entity;
    }
}
