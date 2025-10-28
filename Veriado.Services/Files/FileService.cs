using System.Collections.Generic;
using System.Linq;
using Veriado.Application.Files;
using Veriado.Application.Files.Contracts;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;
using Veriado.Services.Files.Exceptions;

namespace Veriado.Services.Files;

/// <summary>
/// Provides an application-facing façade for loading and updating file details.
/// </summary>
public sealed class FileService : IFileService
{
    private readonly IFileQueryService _fileQueryService;
    private readonly IFileOperationsService _fileOperationsService;

    public FileService(IFileQueryService fileQueryService, IFileOperationsService fileOperationsService)
    {
        _fileQueryService = fileQueryService ?? throw new ArgumentNullException(nameof(fileQueryService));
        _fileOperationsService = fileOperationsService ?? throw new ArgumentNullException(nameof(fileOperationsService));
    }

    public async Task<FileDetailDto> GetDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var detail = await _fileQueryService.GetDetailAsync(id, cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            throw new FileDetailNotFoundException(id);
        }

        return Map(detail);
    }

    public async Task UpdateAsync(FileDetailDto detail, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var current = await _fileQueryService.GetDetailAsync(detail.Id, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            throw new FileDetailNotFoundException(detail.Id);
        }

        if (!string.Equals(current.Name, detail.FileName, StringComparison.Ordinal))
        {
            await EnsureSuccessAsync(
                await _fileOperationsService.RenameAsync(detail.Id, detail.FileName, cancellationToken).ConfigureAwait(false))
                .ConfigureAwait(false);

            current = await _fileQueryService.GetDetailAsync(detail.Id, cancellationToken).ConfigureAwait(false)
                ?? throw new FileDetailNotFoundException(detail.Id);
        }

        var metadataPatch = BuildMetadataPatch(current, detail);
        if (metadataPatch is not null)
        {
            await EnsureSuccessAsync(
                await _fileOperationsService
                    .UpdateMetadataAsync(detail.Id, metadataPatch, current.Version, cancellationToken)
                    .ConfigureAwait(false)).ConfigureAwait(false);

            current = await _fileQueryService.GetDetailAsync(detail.Id, cancellationToken).ConfigureAwait(false)
                ?? throw new FileDetailNotFoundException(detail.Id);
        }

        await UpdateValidityAsync(current, detail, cancellationToken).ConfigureAwait(false);
    }

    private static FileDetailDto Map(Veriado.Contracts.Files.FileDetailDto detail)
    {
        return new FileDetailDto
        {
            Id = detail.Id,
            FileName = detail.Name,
            Extension = detail.Extension,
            MimeType = detail.Mime,
            Author = string.IsNullOrWhiteSpace(detail.Author) ? null : detail.Author,
            IsReadOnly = detail.IsReadOnly,
            Size = detail.Size,
            CreatedAt = detail.CreatedUtc,
            ModifiedAt = detail.LastModifiedUtc,
            Version = detail.Version,
            ValidFrom = detail.Validity?.IssuedAt,
            ValidTo = detail.Validity?.ValidUntil,
        };
    }

    private static FileMetadataPatchDto? BuildMetadataPatch(Veriado.Contracts.Files.FileDetailDto current, FileDetailDto desired)
    {
        string? mimePatch = null;
        string? authorPatch = null;
        bool? isReadOnlyPatch = null;

        if (!string.Equals(current.Mime, desired.MimeType, StringComparison.OrdinalIgnoreCase))
        {
            mimePatch = desired.MimeType;
        }

        if (!string.Equals(current.Author, desired.Author, StringComparison.Ordinal))
        {
            authorPatch = desired.Author;
        }

        if (current.IsReadOnly != desired.IsReadOnly)
        {
            isReadOnlyPatch = desired.IsReadOnly;
        }

        if (mimePatch is null && authorPatch is null && isReadOnlyPatch is null)
        {
            return null;
        }

        return new FileMetadataPatchDto
        {
            Mime = mimePatch,
            Author = authorPatch,
            IsReadOnly = isReadOnlyPatch,
        };
    }

    private async Task UpdateValidityAsync(Veriado.Contracts.Files.FileDetailDto current, FileDetailDto desired, CancellationToken cancellationToken)
    {
        var currentValidity = current.Validity;
        var desiredRange = (ValidFrom: desired.ValidFrom, ValidTo: desired.ValidTo);

        if (desiredRange.ValidFrom is null || desiredRange.ValidTo is null)
        {
            if (currentValidity is null)
            {
                return;
            }

            await EnsureSuccessAsync(
                await _fileOperationsService
                    .ClearValidityAsync(desired.Id, current.Version, cancellationToken)
                    .ConfigureAwait(false)).ConfigureAwait(false);
            return;
        }

        if (currentValidity is not null
            && currentValidity.IssuedAt == desiredRange.ValidFrom
            && currentValidity.ValidUntil == desiredRange.ValidTo)
        {
            return;
        }

        var validity = new FileValidityDto(
            desiredRange.ValidFrom.Value,
            desiredRange.ValidTo.Value,
            currentValidity?.HasPhysicalCopy ?? false,
            currentValidity?.HasElectronicCopy ?? false);

        await EnsureSuccessAsync(
            await _fileOperationsService
                .SetValidityAsync(desired.Id, validity, current.Version, cancellationToken)
                .ConfigureAwait(false)).ConfigureAwait(false);
    }

    private static Task EnsureSuccessAsync(ApiResponse<Guid> response)
    {
        EnsureSuccessCore(response.IsSuccess, response.Errors);
        return Task.CompletedTask;
    }

    private static Task EnsureSuccessAsync(ApiResponse response)
    {
        EnsureSuccessCore(response.IsSuccess, response.Errors);
        return Task.CompletedTask;
    }

    private static void EnsureSuccessCore(bool isSuccess, IReadOnlyList<ApiError> errors)
    {
        if (isSuccess)
        {
            return;
        }

        if (errors.Count == 0)
        {
            throw new FileDetailServiceException("Unexpected failure while processing file detail request.");
        }

        if (errors.Any(IsConflict))
        {
            throw new FileDetailConcurrencyException(errors[0].Message);
        }

        var validationErrors = errors.Where(IsValidation).ToArray();
        if (validationErrors.Length > 0)
        {
            var lookup = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var error in validationErrors)
            {
                var key = error.Target ?? string.Empty;
                if (!lookup.TryGetValue(key, out var messages))
                {
                    lookup[key] = new[] { error.Message };
                }
                else
                {
                    var merged = new string[messages.Length + 1];
                    messages.CopyTo(merged, 0);
                    merged[^1] = error.Message;
                    lookup[key] = merged;
                }
            }

            throw new FileDetailValidationException("Zadaná data nejsou platná.", lookup);
        }

        throw new FileDetailServiceException(errors[0].Message);
    }

    private static bool IsValidation(ApiError error)
        => string.Equals(error.Code, "validation_error", StringComparison.OrdinalIgnoreCase);

    private static bool IsConflict(ApiError error)
        => string.Equals(error.Code, "conflict", StringComparison.OrdinalIgnoreCase);
}
