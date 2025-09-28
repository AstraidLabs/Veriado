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
                Size = file.Size.Value,
                file.Version,
                file.IsReadOnly,
                CreatedUtc = file.CreatedUtc.Value,
                LastModifiedUtc = file.LastModifiedUtc.Value,
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
            projection.Size,
            projection.Version,
            projection.IsReadOnly,
            projection.CreatedUtc,
            projection.LastModifiedUtc,
            validity,
            projection.SystemMetadata);
    }

    public async Task<Page<FileListItemReadModel>> ListAsync(PageRequest request, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var baseQuery = context.Files.AsNoTracking();
        var totalCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await baseQuery
            .OrderByDescending(file => file.LastModifiedUtc.Value)
            .ThenBy(file => file.Id)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(file => new FileListItemReadModel(
                file.Id,
                file.Name.Value,
                file.Extension.Value,
                file.Mime.Value,
                file.Author,
                file.Size.Value,
                file.Version,
                file.IsReadOnly,
                file.CreatedUtc.Value,
                file.LastModifiedUtc.Value,
                file.Validity == null ? (DateTimeOffset?)null : file.Validity.ValidUntil.Value))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new Page<FileListItemReadModel>(items, request.PageNumber, request.PageSize, totalCount);
    }

    public async Task<IReadOnlyList<FileListItemReadModel>> ListExpiringAsync(DateTimeOffset validUntilUtc, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var threshold = validUntilUtc.ToUniversalTime();

        var items = await context.Files
            .AsNoTracking()
            .Where(file => file.Validity != null && file.Validity.ValidUntil.Value <= threshold)
            .OrderBy(file => file.Validity!.ValidUntil.Value)
            .ThenBy(file => file.Id)
            .Select(file => new FileListItemReadModel(
                file.Id,
                file.Name.Value,
                file.Extension.Value,
                file.Mime.Value,
                file.Author,
                file.Size.Value,
                file.Version,
                file.IsReadOnly,
                file.CreatedUtc.Value,
                file.LastModifiedUtc.Value,
                file.Validity!.ValidUntil.Value))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

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
}
