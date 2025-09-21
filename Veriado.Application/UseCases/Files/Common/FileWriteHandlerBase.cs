using System.Threading;
using System.Threading.Tasks;
using Veriado.Application.Abstractions;
using Veriado.Domain.Files;

namespace Veriado.Application.UseCases.Files.Common;

/// <summary>
/// Provides reusable persistence helpers for file write handlers.
/// </summary>
public abstract class FileWriteHandlerBase
{
    private readonly IFileRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ISearchIndexer _searchIndexer;
    private readonly ITextExtractor _textExtractor;
    private readonly IClock _clock;

    protected IFileRepository Repository => _repository;

    protected FileWriteHandlerBase(
        IFileRepository repository,
        IEventPublisher eventPublisher,
        ISearchIndexer searchIndexer,
        ITextExtractor textExtractor,
        IClock clock)
    {
        _repository = repository;
        _eventPublisher = eventPublisher;
        _searchIndexer = searchIndexer;
        _textExtractor = textExtractor;
        _clock = clock;
    }

    protected Task PersistNewAsync(FileEntity file, CancellationToken cancellationToken)
        => PersistInternalAsync(file, addFirst: true, extractContent: true, cancellationToken);

    protected Task PersistNewAsync(FileEntity file, bool extractContent, CancellationToken cancellationToken)
        => PersistInternalAsync(file, addFirst: true, extractContent, cancellationToken);

    protected Task PersistAsync(FileEntity file, CancellationToken cancellationToken)
        => PersistInternalAsync(file, addFirst: false, extractContent: true, cancellationToken);

    protected Task PersistAsync(FileEntity file, bool extractContent, CancellationToken cancellationToken)
        => PersistInternalAsync(file, addFirst: false, extractContent, cancellationToken);

    private async Task PersistInternalAsync(
        FileEntity file,
        bool addFirst,
        bool extractContent,
        CancellationToken cancellationToken)
    {
        if (addFirst)
        {
            await _repository.AddAsync(file, cancellationToken);
        }

        if (!addFirst && file.DomainEvents.Count == 0 && !file.SearchIndex.IsStale)
        {
            return;
        }

        await PublishDomainEventsAsync(file, cancellationToken);
        await IndexAndUpdateAsync(file, extractContent, cancellationToken);
    }

    private async Task IndexAndUpdateAsync(FileEntity file, bool extractContent, CancellationToken cancellationToken)
    {
        var text = extractContent
            ? await _textExtractor.ExtractTextAsync(file, cancellationToken)
            : null;
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
