using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Application.Abstractions;
using Veriado.Application.Files.Commands;
using Veriado.Domain.Files;

namespace Veriado.Application.Files.Handlers;

/// <summary>
/// Handles <see cref="CreateFileCommand"/> operations.
/// </summary>
public sealed class CreateFileHandler
{
    private readonly IFileRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ITextExtractor _textExtractor;
    private readonly ISearchIndexer _searchIndexer;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateFileHandler"/> class.
    /// </summary>
    public CreateFileHandler(
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
    /// Executes the supplied command and persists the created aggregate.
    /// </summary>
    /// <param name="command">The command describing the new file.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created aggregate.</returns>
    public async Task<FileEntity> HandleAsync(CreateFileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var file = FileEntity.CreateNew(
            command.Name,
            command.Extension,
            command.Mime,
            command.Author,
            command.ContentBytes,
            command.MaxContentLength);

        ApplyExtendedMetadata(file, command.ExtendedMetadata);

        if (command.SystemMetadata.HasValue)
        {
            file.ApplySystemMetadata(command.SystemMetadata.Value);
        }

        if (command.IsReadOnly)
        {
            file.SetReadOnly(true);
        }

        await _repository.AddAsync(file, cancellationToken).ConfigureAwait(false);
        await FileCommandHandlerHelpers.PublishAndClearAsync(_eventPublisher, file, cancellationToken).ConfigureAwait(false);

        if (await FileIndexingHelper.ReindexAsync(file, _textExtractor, _searchIndexer, cancellationToken).ConfigureAwait(false))
        {
            await _repository.UpdateAsync(file, cancellationToken).ConfigureAwait(false);
        }

        return file;
    }

    private static void ApplyExtendedMetadata(FileEntity file, IReadOnlyCollection<MetadataPatch> patches)
    {
        if (patches.Count == 0)
        {
            return;
        }

        file.SetExtendedMetadata(builder =>
        {
            foreach (var patch in patches)
            {
                if (patch.IsRemoval)
                {
                    builder.Remove(patch.Key);
                }
                else
                {
                    builder.Set(patch.Key, patch.Value!.Value);
                }
            }
        });
    }
}
