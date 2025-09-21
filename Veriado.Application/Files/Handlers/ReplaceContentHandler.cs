using System;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Application.Abstractions;
using Veriado.Application.Files.Commands;
using Veriado.Domain.Files;

namespace Veriado.Application.Files.Handlers;

/// <summary>
/// Handles <see cref="ReplaceContentCommand"/> operations.
/// </summary>
[Obsolete("Use Veriado.Application.UseCases.Files.ReplaceFileContent.ReplaceFileContentHandler instead.")]
public sealed class ReplaceContentHandler
{
    private readonly IFileRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ITextExtractor _textExtractor;
    private readonly ISearchIndexer _searchIndexer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplaceContentHandler"/> class.
    /// </summary>
    public ReplaceContentHandler(
        IFileRepository repository,
        IEventPublisher eventPublisher,
        ITextExtractor textExtractor,
        ISearchIndexer searchIndexer)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _textExtractor = textExtractor ?? throw new ArgumentNullException(nameof(textExtractor));
        _searchIndexer = searchIndexer ?? throw new ArgumentNullException(nameof(searchIndexer));
    }

    /// <summary>
    /// Executes the supplied command and persists the changes.
    /// </summary>
    /// <param name="command">The command describing the content replacement.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated aggregate.</returns>
    public async Task<FileEntity> HandleAsync(ReplaceContentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var file = await _repository.GetAsync(command.FileId, cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            throw new InvalidOperationException($"File '{command.FileId}' was not found.");
        }

        file.ReplaceContent(command.ContentBytes, command.MaxContentLength);
        if (file.DomainEvents.Count == 0)
        {
            return file;
        }

        await FileIndexingHelper.ReindexAsync(file, _textExtractor, _searchIndexer, cancellationToken).ConfigureAwait(false);
        await _repository.UpdateAsync(file, cancellationToken).ConfigureAwait(false);
        await FileCommandHandlerHelpers.PublishAndClearAsync(_eventPublisher, file, cancellationToken).ConfigureAwait(false);
        return file;
    }
}
