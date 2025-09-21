using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Veriado.Application.Search.Abstractions;
using Veriado.Application.UseCases.Queries;
using Veriado.Application.UseCases.Queries.FileGrid;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;
using Veriado.Services.Files.Models;

using AppFileDetailDto = Veriado.Application.DTO.FileDetailDto;
using AppFileSystemMetadataDto = Veriado.Application.DTO.FileSystemMetadataDto;
using AppFileValidityDto = Veriado.Application.DTO.FileValidityDto;

namespace Veriado.Services.Files;

/// <summary>
/// Implements read-oriented orchestration over the file catalogue.
/// </summary>
public sealed class FileQueryService : IFileQueryService
{
    private readonly IMediator _mediator;
    private readonly ISearchHistoryService _historyService;
    private readonly ISearchFavoritesService _favoritesService;

    public FileQueryService(
        IMediator mediator,
        ISearchHistoryService historyService,
        ISearchFavoritesService favoritesService)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        _favoritesService = favoritesService ?? throw new ArgumentNullException(nameof(favoritesService));
    }

    public Task<PageResult<FileSummaryDto>> GetGridAsync(FileGridQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _mediator.Send(query, cancellationToken);
    }

    public async Task<FileDetailDto?> GetDetailAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var detail = await _mediator.Send(new GetFileDetailQuery(fileId), cancellationToken).ConfigureAwait(false);
        return detail is null ? null : MapToContract(detail);
    }

    public Task<IReadOnlyList<SearchHistoryEntry>> GetSearchHistoryAsync(int take, CancellationToken cancellationToken)
    {
        return _historyService.GetRecentAsync(take, cancellationToken);
    }

    public Task<IReadOnlyList<SearchFavoriteItem>> GetFavoritesAsync(CancellationToken cancellationToken)
    {
        return _favoritesService.GetAllAsync(cancellationToken);
    }

    public Task AddFavoriteAsync(SearchFavoriteDefinition favorite, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(favorite);
        return _favoritesService.AddAsync(favorite.Name, favorite.MatchQuery, favorite.QueryText, favorite.IsFuzzy, cancellationToken);
    }

    public Task RemoveFavoriteAsync(Guid favoriteId, CancellationToken cancellationToken)
    {
        return _favoritesService.RemoveAsync(favoriteId, cancellationToken);
    }

    private static FileDetailDto MapToContract(AppFileDetailDto detail)
    {
        var file = detail.File;

        return new FileDetailDto
        {
            Id = file.Id,
            Name = file.Name,
            Extension = file.Extension,
            Mime = file.Mime,
            Author = file.Author,
            Size = file.SizeBytes,
            CreatedUtc = file.CreatedUtc,
            LastModifiedUtc = file.LastModifiedUtc,
            IsReadOnly = file.IsReadOnly,
            Version = file.Version,
            Content = new FileContentDto(string.Empty, file.SizeBytes),
            SystemMetadata = MapSystemMetadata(detail.SystemMetadata),
            ExtendedMetadata = MapExtendedMetadata(detail.ExtendedMetadata),
            Validity = MapValidity(file.Validity),
        };
    }

    private static FileSystemMetadataDto MapSystemMetadata(AppFileSystemMetadataDto metadata)
    {
        return new FileSystemMetadataDto(
            (int)metadata.Attributes,
            metadata.CreatedUtc,
            metadata.LastWriteUtc,
            metadata.LastAccessUtc,
            metadata.OwnerSid,
            metadata.HardLinkCount,
            metadata.AlternateDataStreamCount);
    }

    private static FileValidityDto? MapValidity(AppFileValidityDto? validity)
    {
        if (validity is null)
        {
            return null;
        }

        return new FileValidityDto(
            validity.IssuedAtUtc,
            validity.ValidUntilUtc,
            validity.HasPhysicalCopy,
            validity.HasElectronicCopy);
    }

    private static IReadOnlyList<ExtendedMetadataItemDto> MapExtendedMetadata(IReadOnlyDictionary<string, string?> metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return Array.Empty<ExtendedMetadataItemDto>();
        }

        var items = new List<ExtendedMetadataItemDto>(metadata.Count);
        foreach (var pair in metadata)
        {
            if (!TryParsePropertyKey(pair.Key, out var formatId, out var propertyId))
            {
                continue;
            }

            var valueDto = pair.Value is null
                ? new MetadataValueDto { Kind = MetadataValueDtoKind.Null }
                : new MetadataValueDto
                {
                    Kind = MetadataValueDtoKind.String,
                    StringValue = pair.Value,
                };

            items.Add(new ExtendedMetadataItemDto
            {
                FormatId = formatId,
                PropertyId = propertyId,
                Value = valueDto,
                Remove = false,
            });
        }

        if (items.Count == 0)
        {
            return Array.Empty<ExtendedMetadataItemDto>();
        }

        return items;
    }

    private static bool TryParsePropertyKey(string key, out Guid formatId, out int propertyId)
    {
        formatId = Guid.Empty;
        propertyId = 0;

        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var parts = key.Split('/', 2);
        if (parts.Length != 2)
        {
            return false;
        }

        if (!Guid.TryParse(parts[0], out formatId))
        {
            return false;
        }

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out propertyId))
        {
            formatId = Guid.Empty;
            return false;
        }

        return true;
    }
}
