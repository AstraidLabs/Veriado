using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Veriado.Application.Abstractions;
using Veriado.Application.Common;
using Veriado.Application.UseCases.Files.ApplySystemMetadata;
using Veriado.Application.UseCases.Files.ClearFileValidity;
using Veriado.Application.UseCases.Files.ReplaceFileContent;
using Veriado.Application.UseCases.Files.RenameFile;
using Veriado.Application.UseCases.Files.SetFileReadOnly;
using Veriado.Application.UseCases.Files.SetFileValidity;
using Veriado.Application.UseCases.Maintenance;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;
using Veriado.Mapping.AC;

using AppFileDto = Veriado.Application.DTO.FileDto;

namespace Veriado.Services.Files;

/// <summary>
/// Implements orchestration helpers over file write operations.
/// </summary>
public sealed class FileOperationsService : IFileOperationsService
{
    private readonly IMediator _mediator;
    private readonly WriteMappingPipeline _mappingPipeline;
    private readonly IRequestContext _requestContext;

    public FileOperationsService(IMediator mediator, WriteMappingPipeline mappingPipeline, IRequestContext requestContext)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _mappingPipeline = mappingPipeline ?? throw new ArgumentNullException(nameof(mappingPipeline));
        _requestContext = requestContext ?? throw new ArgumentNullException(nameof(requestContext));
    }

    public async Task<AppResult<Guid>> RenameAsync(Guid fileId, string newName, CancellationToken cancellationToken)
    {
        var mapping = await _mappingPipeline.MapRenameAsync(fileId, newName, cancellationToken).ConfigureAwait(false);
        if (!mapping.IsSuccess)
        {
            return ValidationFailure(mapping.Errors);
        }

        using var scope = BeginScope();
        var result = await _mediator.Send(mapping.Data!, cancellationToken).ConfigureAwait(false);
        return ToIdResult(result);
    }

    public async Task<AppResult<Guid>> UpdateMetadataAsync(UpdateMetadataRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var mapping = await _mappingPipeline.MapUpdateMetadataAsync(request, cancellationToken).ConfigureAwait(false);
        if (!mapping.IsSuccess)
        {
            return ValidationFailure(mapping.Errors);
        }

        using var scope = BeginScope();
        AppResult<AppFileDto>? lastResult = null;
        foreach (var command in mapping.Data!.Commands())
        {
            var commandResult = await _mediator.Send(command, cancellationToken).ConfigureAwait(false);
            if (commandResult.IsFailure)
            {
                return ToIdResult(commandResult);
            }

            lastResult = commandResult;
        }

        if (lastResult is { } finalResult)
        {
            return ToIdResult(finalResult);
        }

        return AppResult<Guid>.Success(request.FileId);
    }

    public async Task<AppResult<Guid>> SetReadOnlyAsync(Guid fileId, bool isReadOnly, CancellationToken cancellationToken)
    {
        using var scope = BeginScope();
        var result = await _mediator
            .Send(new SetFileReadOnlyCommand(fileId, isReadOnly), cancellationToken)
            .ConfigureAwait(false);
        return ToIdResult(result);
    }

    public async Task<AppResult<Guid>> SetValidityAsync(Guid fileId, FileValidityDto validity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(validity);
        var request = new SetValidityRequest
        {
            FileId = fileId,
            IssuedAt = validity.IssuedAt,
            ValidUntil = validity.ValidUntil,
            HasElectronicCopy = validity.HasElectronicCopy,
            HasPhysicalCopy = validity.HasPhysicalCopy,
        };

        var mapping = await _mappingPipeline.MapSetValidityAsync(request, cancellationToken).ConfigureAwait(false);
        if (!mapping.IsSuccess)
        {
            return ValidationFailure(mapping.Errors);
        }

        using var scope = BeginScope();
        var result = await _mediator.Send(mapping.Data!, cancellationToken).ConfigureAwait(false);
        return ToIdResult(result);
    }

    public async Task<AppResult<Guid>> ClearValidityAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var mapping = await _mappingPipeline
            .MapClearValidityAsync(new ClearValidityRequest { FileId = fileId }, cancellationToken)
            .ConfigureAwait(false);
        if (!mapping.IsSuccess)
        {
            return ValidationFailure(mapping.Errors);
        }

        using var scope = BeginScope();
        var result = await _mediator.Send(mapping.Data!, cancellationToken).ConfigureAwait(false);
        return ToIdResult(result);
    }

    public async Task<AppResult<Guid>> ReplaceContentAsync(Guid fileId, byte[] content, bool extractContent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var request = new ReplaceContentRequest
        {
            FileId = fileId,
            Content = content,
        };

        var mapping = await _mappingPipeline.MapReplaceContentAsync(request, cancellationToken).ConfigureAwait(false);
        if (!mapping.IsSuccess)
        {
            return ValidationFailure(mapping.Errors);
        }

        using var scope = BeginScope();
        var result = await _mediator.Send(mapping.Data!, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return ToIdResult(result);
        }

        if (!extractContent)
        {
            var reindexResult = await _mediator
                .Send(new ReindexFileCommand(fileId, ExtractContent: false), cancellationToken)
                .ConfigureAwait(false);
            if (reindexResult.IsFailure)
            {
                return ToIdResult(reindexResult);
            }
        }

        return ToIdResult(result);
    }

    public async Task<AppResult<Guid>> ApplySystemMetadataAsync(Guid fileId, FileSystemMetadataDto metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        using var scope = BeginScope();
        var command = new ApplySystemMetadataCommand(
            fileId,
            (Veriado.Domain.Metadata.FileAttributesFlags)metadata.Attributes,
            metadata.CreatedUtc,
            metadata.LastWriteUtc,
            metadata.LastAccessUtc,
            metadata.OwnerSid,
            metadata.HardLinkCount,
            metadata.AlternateDataStreamCount);

        var result = await _mediator.Send(command, cancellationToken).ConfigureAwait(false);
        return ToIdResult(result);
    }
    private AppResult<Guid> ValidationFailure(IReadOnlyList<ApiError> errors)
    {
        if (errors.Count == 0)
        {
            return AppResult<Guid>.Validation(Array.Empty<string>());
        }

        var messages = errors.Select(error =>
            string.IsNullOrWhiteSpace(error.Target)
                ? error.Message
                : $"{error.Target}: {error.Message}").ToArray();
        return AppResult<Guid>.Validation(messages);
    }

    private static AppResult<Guid> ToIdResult(AppResult<AppFileDto> result)
    {
        return result.IsSuccess
            ? AppResult<Guid>.Success(result.Value.Id)
            : AppResult<Guid>.Failure(result.Error);
    }

    private IDisposable BeginScope()
    {
        return AmbientRequestContext.Begin(Guid.NewGuid(), _requestContext.UserId, _requestContext.CorrelationId);
    }
}
