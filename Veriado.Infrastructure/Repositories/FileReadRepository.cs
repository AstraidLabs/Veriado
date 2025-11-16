using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Veriado.Appl.Common;

namespace Veriado.Infrastructure.Repositories;

/// <summary>
/// Provides read-only queries using the pooled <see cref="ReadOnlyDbContext"/>.
/// </summary>
internal sealed class FileReadRepository : IFileReadRepository
{
    private readonly IDbContextFactory<ReadOnlyDbContext> _contextFactory;

    public FileReadRepository(IDbContextFactory<ReadOnlyDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<FileDetailReadModel?> GetDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var projection = await context.Files
            .AsNoTracking()
            .Where(file => file.Id == id)
            .Select(file => new
            {
                file.Id,
                Name = file.Name.Value,
                Title = file.Title ?? file.Name.Value,
                Extension = file.Extension.Value,
                Mime = file.Mime.Value,
                file.Author,
                Size = file.Content != null ? (long?)file.Content.Size.Value : null,
                Version = file.ContentRevision,
                file.IsReadOnly,
                CreatedUtc = EF.Property<string>(file, nameof(FileEntity.CreatedUtc)),
                LastModifiedUtc = EF.Property<string>(file, nameof(FileEntity.LastModifiedUtc)),
                Validity = file.Validity,
                file.SystemMetadata,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (projection is null)
        {
            return null;
        }

        var validity = MapValidity(projection.Validity);

        return new FileDetailReadModel(
            projection.Id,
            projection.Name,
            projection.Title,
            projection.Extension,
            projection.Mime,
            projection.Author,
            projection.Size ?? 0L,
            projection.Version,
            projection.IsReadOnly,
            ParseTimestamp(projection.CreatedUtc),
            ParseTimestamp(projection.LastModifiedUtc),
            validity,
            projection.SystemMetadata);
    }

    public async Task<Page<FileListItemReadModel>> ListAsync(PageRequest request, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var baseQuery = context.Files.AsNoTracking();
        var totalCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        var rows = await baseQuery
            .OrderByDescending(file => EF.Property<string>(file, nameof(FileEntity.LastModifiedUtc)))
            .ThenBy(file => file.Id)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(file => new
            {
                file.Id,
                Name = file.Name.Value,
                Extension = file.Extension.Value,
                Mime = file.Mime.Value,
                file.Author,
                Size = file.Content != null ? (long?)file.Content.Size.Value : null,
                file.ContentRevision,
                file.IsReadOnly,
                CreatedUtc = EF.Property<string>(file, nameof(FileEntity.CreatedUtc)),
                LastModifiedUtc = EF.Property<string>(file, nameof(FileEntity.LastModifiedUtc)),
                ValidUntil = EF.Property<DateTimeOffset?>(file, "Validity_ValidUntil"),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = rows
            .Select(row => new FileListItemReadModel(
                row.Id,
                row.Name,
                row.Extension,
                row.Mime,
                row.Author,
                row.Size ?? 0L,
                row.ContentRevision,
                row.IsReadOnly,
                ParseTimestamp(row.CreatedUtc),
                ParseTimestamp(row.LastModifiedUtc),
                row.ValidUntil))
            .ToList();

        return new Page<FileListItemReadModel>(items, request.PageNumber, request.PageSize, totalCount);
    }

    public async Task<IReadOnlyList<FileListItemReadModel>> ListExpiringAsync(DateTimeOffset validUntilUtc, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var threshold = validUntilUtc.ToUniversalTime();

        var rows = await context.Files
            .AsNoTracking()
            .Where(file => file.Validity != null
                && EF.Property<DateTimeOffset?>(file, "Validity_ValidUntil") <= threshold)
            .OrderBy(file => EF.Property<DateTimeOffset?>(file, "Validity_ValidUntil"))
            .ThenBy(file => file.Id)
            .Select(file => new
            {
                file.Id,
                Name = file.Name.Value,
                Extension = file.Extension.Value,
                Mime = file.Mime.Value,
                file.Author,
                Size = file.Content != null ? (long?)file.Content.Size.Value : null,
                file.ContentRevision,
                file.IsReadOnly,
                CreatedUtc = EF.Property<string>(file, nameof(FileEntity.CreatedUtc)),
                LastModifiedUtc = EF.Property<string>(file, nameof(FileEntity.LastModifiedUtc)),
                ValidUntil = EF.Property<DateTimeOffset?>(file, "Validity_ValidUntil"),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = rows
            .Select(row => new FileListItemReadModel(
                row.Id,
                row.Name,
                row.Extension,
                row.Mime,
                row.Author,
                row.Size ?? 0L,
                row.ContentRevision,
                row.IsReadOnly,
                ParseTimestamp(row.CreatedUtc),
                ParseTimestamp(row.LastModifiedUtc),
                row.ValidUntil))
            .ToList();

        return items;
    }

    private static FileDocumentValidityReadModel? MapValidity(FileDocumentValidityEntity? validity)
    {
        if (validity is null)
        {
            return null;
        }

        return new FileDocumentValidityReadModel(
            validity.IssuedAt.Value,
            validity.ValidUntil.Value,
            validity.HasPhysicalCopy,
            validity.HasElectronicCopy);
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}
