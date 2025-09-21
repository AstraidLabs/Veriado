using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Veriado.Application.Abstractions;
using Veriado.Application.Common;
using Veriado.Domain.Files;
using Veriado.Domain.Metadata;
using Veriado.Infrastructure.Persistence;

namespace Veriado.Infrastructure.Repositories;

/// <summary>
/// Provides read-only queries using the pooled <see cref="ReadOnlyDbContext"/>.
/// </summary>
internal sealed class FileReadRepository : IFileReadRepository
{
    private static readonly FieldInfo MetadataValueField = typeof(MetadataValue)
        .GetField("_value", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("MetadataValue internal layout has changed.");

    private readonly IDbContextFactory<ReadOnlyDbContext> _contextFactory;

    public FileReadRepository(IDbContextFactory<ReadOnlyDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<FileDetailReadModel?> GetDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var entity = await context.Files
            .AsNoTracking()
            .Include(file => file.Validity)
            .FirstOrDefaultAsync(file => file.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : MapToDetail(entity);
    }

    public async Task<Page<FileListItemReadModel>> ListAsync(PageRequest request, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var baseQuery = context.Files.AsNoTracking();

        var totalCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        var entities = await baseQuery
            .Include(file => file.Validity)
            .OrderByDescending(file => file.LastModifiedUtc.Value)
            .ThenBy(file => file.Id)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = entities.Select(MapToListItem).ToList();

        return new Page<FileListItemReadModel>(items, request.PageNumber, request.PageSize, totalCount);
    }

    public async Task<IReadOnlyList<FileListItemReadModel>> ListExpiringAsync(DateTimeOffset validUntilUtc, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var threshold = validUntilUtc.ToUniversalTime();

        var entities = await context.Files
            .AsNoTracking()
            .Include(file => file.Validity)
            .Where(file => file.Validity != null && file.Validity.ValidUntil.Value <= threshold)
            .OrderBy(file => file.Validity!.ValidUntil.Value)
            .ThenBy(file => file.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.Select(MapToListItem).ToArray();
    }

    private static FileDetailReadModel MapToDetail(FileEntity file)
    {
        var validity = MapValidity(file.Validity);
        var metadata = MapExtendedMetadata(file.ExtendedMetadata);

        return new FileDetailReadModel(
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
            validity,
            file.SystemMetadata,
            metadata);
    }

    private static FileListItemReadModel MapToListItem(FileEntity file)
    {
        return new FileListItemReadModel(
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
            file.Validity?.ValidUntil.Value);
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

    private static IReadOnlyDictionary<string, string?> MapExtendedMetadata(ExtendedMetadata metadata)
    {
        var dictionary = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var pair in metadata.AsEnumerable())
        {
            dictionary[pair.Key.ToString()] = FormatMetadataValue(pair.Value);
        }

        return dictionary;
    }

    private static string? FormatMetadataValue(MetadataValue value)
    {
        if (value.TryGetString(out var single))
        {
            return single;
        }

        if (value.TryGetStringArray(out var array) && array is not null)
        {
            return string.Join(", ", array);
        }

        if (value.TryGetGuid(out var guid))
        {
            return guid.ToString("D", CultureInfo.InvariantCulture);
        }

        if (value.TryGetFileTime(out var fileTime))
        {
            return fileTime.ToString("O", CultureInfo.InvariantCulture);
        }

        if (value.TryGetBinary(out var binary) && binary is not null)
        {
            return Convert.ToBase64String(binary);
        }

        var raw = MetadataValueField.GetValue(value);
        return raw switch
        {
            null => null,
            bool boolean => boolean.ToString(CultureInfo.InvariantCulture),
            int number => number.ToString(CultureInfo.InvariantCulture),
            uint unsigned => unsigned.ToString(CultureInfo.InvariantCulture),
            double dbl => dbl.ToString(CultureInfo.InvariantCulture),
            _ => raw.ToString(),
        };
    }
}
