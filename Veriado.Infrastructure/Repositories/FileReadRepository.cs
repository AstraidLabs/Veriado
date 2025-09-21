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
using Veriado.Infrastructure.MetadataStore.Kv;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Options;

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
    private readonly InfrastructureOptions _options;

    public FileReadRepository(IDbContextFactory<ReadOnlyDbContext> contextFactory, InfrastructureOptions options)
    {
        _contextFactory = contextFactory;
        _options = options;
    }

    public async Task<FileDetailReadModel?> GetDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        if (_options.UseKvMetadata)
        {
            var projection = await context.Files
                .AsNoTracking()
                .Where(file => file.Id == id)
                .Select(file => new
                {
                    file.Id,
                    Name = file.Name.Value,
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

            var metadataEntries = await context.ExtendedMetadataEntries
                .Where(entry => entry.FileId == id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var metadata = MapExtendedMetadata(ExtMetadataMapper.FromEntries(metadataEntries));
            var validity = MapValidity(projection.Validity);

            return new FileDetailReadModel(
                projection.Id,
                projection.Name,
                projection.Extension,
                projection.Mime,
                projection.Author,
                projection.Size,
                projection.Version,
                projection.IsReadOnly,
                projection.CreatedUtc,
                projection.LastModifiedUtc,
                validity,
                projection.SystemMetadata,
                metadata);
        }

        var defaultProjection = await context.Files
            .AsNoTracking()
            .Where(file => file.Id == id)
            .Select(file => new
            {
                file.Id,
                Name = file.Name.Value,
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
                file.ExtendedMetadata,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (defaultProjection is null)
        {
            return null;
        }

        var defaultValidity = MapValidity(defaultProjection.Validity);
        var defaultMetadata = MapExtendedMetadata(defaultProjection.ExtendedMetadata);

        return new FileDetailReadModel(
            defaultProjection.Id,
            defaultProjection.Name,
            defaultProjection.Extension,
            defaultProjection.Mime,
            defaultProjection.Author,
            defaultProjection.Size,
            defaultProjection.Version,
            defaultProjection.IsReadOnly,
            defaultProjection.CreatedUtc,
            defaultProjection.LastModifiedUtc,
            defaultValidity,
            defaultProjection.SystemMetadata,
            defaultMetadata);
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
