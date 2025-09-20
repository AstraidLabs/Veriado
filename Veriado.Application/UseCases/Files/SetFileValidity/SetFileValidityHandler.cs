using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Veriado.Application.Abstractions;
using Veriado.Application.Common;
using Veriado.Application.DTO;
using Veriado.Application.Mapping;
using Veriado.Domain.Files;
using Veriado.Domain.ValueObjects;

namespace Veriado.Application.UseCases.Files.SetFileValidity;

/// <summary>
/// Handles updates to document validity metadata for files.
/// </summary>
public sealed class SetFileValidityHandler : IRequestHandler<SetFileValidityCommand, AppResult<FileDto>>
{
    private readonly IFileRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ISearchIndexer _searchIndexer;
    private readonly IClock _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="SetFileValidityHandler"/> class.
    /// </summary>
    public SetFileValidityHandler(
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
    public async Task<AppResult<FileDto>> Handle(SetFileValidityCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var file = await _repository.GetAsync(request.FileId, cancellationToken);
            if (file is null)
            {
                return AppResult<FileDto>.NotFound($"File '{request.FileId}' was not found.");
            }

            var issued = UtcTimestamp.From(request.IssuedAtUtc);
            var validUntil = UtcTimestamp.From(request.ValidUntilUtc);
            file.SetValidity(issued, validUntil, request.HasPhysicalCopy, request.HasElectronicCopy);
            await PersistAsync(file, cancellationToken);
            return AppResult<FileDto>.Success(DomainToDto.ToFileDto(file));
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            return AppResult<FileDto>.FromException(ex);
        }
        catch (InvalidOperationException ex)
        {
            return AppResult<FileDto>.FromException(ex);
        }
        catch (Exception ex)
        {
            return AppResult<FileDto>.FromException(ex, "Failed to update file validity.");
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
