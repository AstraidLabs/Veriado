using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Veriado.Application.Abstractions;
using Veriado.Application.Common;
using Veriado.Application.DTO;
using Veriado.Application.Mapping;
using Veriado.Domain.Files;

namespace Veriado.Application.UseCases.Files.ClearFileValidity;

/// <summary>
/// Handles clearing document validity from files.
/// </summary>
public sealed class ClearFileValidityHandler : IRequestHandler<ClearFileValidityCommand, AppResult<FileDto>>
{
    private readonly IFileRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ISearchIndexer _searchIndexer;
    private readonly IClock _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClearFileValidityHandler"/> class.
    /// </summary>
    public ClearFileValidityHandler(
        IFileRepository repository,
        IEventPublisher eventPublisher,
        ISearchIndexer searchIndexer,
        IClock clock)
    {
        _repository = repository;
        _eventPublisher = eventPublisher;
        _searchIndexer = searchIndexer;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<AppResult<FileDto>> Handle(ClearFileValidityCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var file = await _repository.GetAsync(request.FileId, cancellationToken);
            if (file is null)
            {
                return AppResult<FileDto>.NotFound($"File '{request.FileId}' was not found.");
            }

            file.ClearValidity();
            await PersistAsync(file, cancellationToken);
            return AppResult<FileDto>.Success(DomainToDto.ToFileDto(file));
        }
        catch (InvalidOperationException ex)
        {
            return AppResult<FileDto>.FromException(ex);
        }
        catch (Exception ex)
        {
            return AppResult<FileDto>.FromException(ex, "Failed to clear file validity.");
        }
    }

    private async Task PersistAsync(FileEntity file, CancellationToken cancellationToken)
    {
        await PublishDomainEventsAsync(file, cancellationToken);
        await IndexAndUpdateAsync(file, cancellationToken);
    }

    private async Task IndexAndUpdateAsync(FileEntity file, CancellationToken cancellationToken)
    {
        var document = file.ToSearchDocument();
        await _searchIndexer.IndexAsync(document, cancellationToken);
        file.ConfirmIndexed(file.SearchIndex.SchemaVersion, _clock.UtcNow);
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
