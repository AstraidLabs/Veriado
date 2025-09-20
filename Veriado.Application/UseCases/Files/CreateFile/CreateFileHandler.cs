using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Veriado.Application.Abstractions;
using Veriado.Application.Common;
using Veriado.Application.Common.Policies;
using Veriado.Domain.Files;
using Veriado.Domain.ValueObjects;

namespace Veriado.Application.UseCases.Files.CreateFile;

/// <summary>
/// Handles creation of new file aggregates.
/// </summary>
public sealed class CreateFileHandler : IRequestHandler<CreateFileCommand, AppResult<Guid>>
{
    private readonly IFileRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ISearchIndexer _searchIndexer;
    private readonly ITextExtractor _textExtractor;
    private readonly ImportPolicy _importPolicy;
    private readonly IClock _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateFileHandler"/> class.
    /// </summary>
    public CreateFileHandler(
        IFileRepository repository,
        IEventPublisher eventPublisher,
        ISearchIndexer searchIndexer,
        ITextExtractor textExtractor,
        ImportPolicy importPolicy,
        IClock clock)
    {
        _repository = repository;
        _eventPublisher = eventPublisher;
        _searchIndexer = searchIndexer;
        _textExtractor = textExtractor;
        _importPolicy = importPolicy;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<AppResult<Guid>> Handle(CreateFileCommand request, CancellationToken cancellationToken)
    {
        try
        {
            Guard.AgainstNull(request.Content, nameof(request.Content));
            _importPolicy.EnsureWithinLimit(request.Content.LongLength);

            var name = FileName.From(request.Name);
            var extension = FileExtension.From(request.Extension);
            var mime = MimeType.From(request.Mime);
            var file = FileEntity.CreateNew(name, extension, mime, request.Author, request.Content, _importPolicy.MaxContentLengthBytes);

            await PersistAsync(file, isNew: true, extractContent: true, cancellationToken);
            return AppResult<Guid>.Success(file.Id);
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            return AppResult<Guid>.FromException(ex);
        }
        catch (InvalidOperationException ex)
        {
            return AppResult<Guid>.FromException(ex);
        }
        catch (Exception ex)
        {
            return AppResult<Guid>.FromException(ex, "Failed to create the file.");
        }
    }

    private async Task PersistAsync(FileEntity file, bool isNew, bool extractContent, CancellationToken cancellationToken)
    {
        if (isNew)
        {
            await _repository.AddAsync(file, cancellationToken);
        }

        await PublishDomainEventsAsync(file, cancellationToken);
        await IndexAndUpdateAsync(file, extractContent, cancellationToken);
    }

    private async Task IndexAndUpdateAsync(FileEntity file, bool extractContent, CancellationToken cancellationToken)
    {
        var text = extractContent ? await _textExtractor.ExtractTextAsync(file, cancellationToken) : null;
        var document = file.ToSearchDocument(text);
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
