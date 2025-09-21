using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Veriado.Application.Abstractions;
using Veriado.Application.Common;
using Veriado.Application.DTO;
using Veriado.Application.Mapping;
using Veriado.Domain.Files;

namespace Veriado.Application.UseCases.Maintenance;

/// <summary>
/// Handles explicit reindexing requests for a file.
/// </summary>
public sealed class ReindexFileHandler : IRequestHandler<ReindexFileCommand, AppResult<FileDto>>
{
    private readonly IFileRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ISearchIndexCoordinator _indexCoordinator;
    private readonly IClock _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReindexFileHandler"/> class.
    /// </summary>
    public ReindexFileHandler(
        IFileRepository repository,
        IEventPublisher eventPublisher,
        ISearchIndexCoordinator indexCoordinator,
        IClock clock)
    {
        _repository = repository;
        _eventPublisher = eventPublisher;
        _indexCoordinator = indexCoordinator;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<AppResult<FileDto>> Handle(ReindexFileCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var file = await _repository.GetAsync(request.FileId, cancellationToken);
            if (file is null)
            {
                return AppResult<FileDto>.NotFound($"File '{request.FileId}' was not found.");
            }

            await PublishDomainEventsAsync(file, cancellationToken);
            await IndexAsync(file, request.ExtractContent, cancellationToken);
            return AppResult<FileDto>.Success(DomainToDto.ToFileDto(file));
        }
        catch (Exception ex)
        {
            return AppResult<FileDto>.FromException(ex, "Failed to reindex the file.");
        }
    }

    private async Task IndexAsync(FileEntity file, bool extractContent, CancellationToken cancellationToken)
    {
        var indexed = await _indexCoordinator.IndexAsync(file, extractContent, allowDeferred: false, cancellationToken)
            .ConfigureAwait(false);
        if (indexed)
        {
            file.ConfirmIndexed(file.SearchIndex.SchemaVersion, _clock.UtcNow);
        }
        await _repository.UpdateAsync(file, cancellationToken);
    }

    private async Task PublishDomainEventsAsync(FileEntity file, CancellationToken cancellationToken)
    {
        if (file.DomainEvents.Count == 0)
        {
            return;
        }

        await _eventPublisher.PublishAsync(file.DomainEvents, cancellationToken);
        file.ClearDomainEvents();
    }
}
