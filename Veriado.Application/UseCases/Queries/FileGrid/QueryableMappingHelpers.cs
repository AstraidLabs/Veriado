using System;
using System.Linq;
using System.Linq.Expressions;
using Veriado.Contracts.Files;
using Veriado.Domain.Files;

namespace Veriado.Application.UseCases.Queries.FileGrid;

/// <summary>
/// Provides reusable Entity Framework friendly projections for file aggregates.
/// </summary>
public static class QueryableMappingHelpers
{
    /// <summary>
    /// Gets an expression projecting a <see cref="FileEntity"/> to a <see cref="FileSummaryDto"/>.
    /// </summary>
    public static Expression<Func<FileEntity, FileSummaryDto>> FileSummaryProjection() => file => new FileSummaryDto
    {
        Id = file.Id,
        Name = file.Name.Value,
        Extension = file.Extension.Value,
        Mime = file.Mime.Value,
        Author = file.Author,
        Size = file.Size.Value,
        CreatedUtc = file.CreatedUtc.Value,
        LastModifiedUtc = file.LastModifiedUtc.Value,
        IsReadOnly = file.IsReadOnly,
        Version = file.Version,
        Validity = file.Validity == null
            ? null
            : new FileValidityDto(
                file.Validity.IssuedAt.Value,
                file.Validity.ValidUntil.Value,
                file.Validity.HasPhysicalCopy,
                file.Validity.HasElectronicCopy),
        IsIndexStale = file.SearchIndex.IsStale,
        LastIndexedUtc = file.SearchIndex.LastIndexedUtc,
        IndexedTitle = file.SearchIndex.IndexedTitle,
        IndexSchemaVersion = file.SearchIndex.SchemaVersion,
        IndexedContentHash = file.SearchIndex.IndexedContentHash,
        Score = null,
    };

    /// <summary>
    /// Projects an <see cref="IQueryable{T}"/> of <see cref="FileEntity"/> instances to <see cref="FileSummaryDto"/>s.
    /// </summary>
    public static IQueryable<FileSummaryDto> ProjectToFileSummary(this IQueryable<FileEntity> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.Select(FileSummaryProjection());
    }

    /// <summary>
    /// Gets an expression projecting a <see cref="FileEntity"/> to a lightweight <see cref="FileDetailDto"/> without materializing binary content.
    /// </summary>
    public static Expression<Func<FileEntity, FileDetailDto>> FileDetailProjection() => file => new FileDetailDto
    {
        Id = file.Id,
        Name = file.Name.Value,
        Extension = file.Extension.Value,
        Mime = file.Mime.Value,
        Author = file.Author,
        Size = file.Size.Value,
        CreatedUtc = file.CreatedUtc.Value,
        LastModifiedUtc = file.LastModifiedUtc.Value,
        IsReadOnly = file.IsReadOnly,
        Version = file.Version,
        Content = new FileContentDto(
            file.SearchIndex.IndexedContentHash ?? string.Empty,
            file.Size.Value,
            null),
        SystemMetadata = new FileSystemMetadataDto(
            (int)file.SystemMetadata.Attributes,
            file.SystemMetadata.CreatedUtc.Value,
            file.SystemMetadata.LastWriteUtc.Value,
            file.SystemMetadata.LastAccessUtc.Value,
            file.SystemMetadata.OwnerSid,
            file.SystemMetadata.HardLinkCount,
            file.SystemMetadata.AlternateDataStreamCount),
        ExtendedMetadata = Array.Empty<ExtendedMetadataItemDto>(),
        Validity = file.Validity == null
            ? null
            : new FileValidityDto(
                file.Validity.IssuedAt.Value,
                file.Validity.ValidUntil.Value,
                file.Validity.HasPhysicalCopy,
                file.Validity.HasElectronicCopy),
        IsIndexStale = file.SearchIndex.IsStale,
        LastIndexedUtc = file.SearchIndex.LastIndexedUtc,
        IndexedTitle = file.SearchIndex.IndexedTitle,
        IndexSchemaVersion = file.SearchIndex.SchemaVersion,
        IndexedContentHash = file.SearchIndex.IndexedContentHash,
    };
}
