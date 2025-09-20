using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Application.Abstractions;
using Veriado.Application.Files.Commands;
using Veriado.Domain.Files;

namespace Veriado.Application.Files.Handlers;

/// <summary>
/// Handles <see cref="UpdateMetadataCommand"/> operations.
/// </summary>
public sealed class UpdateMetadataHandler
{
    private readonly IFileRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ITextExtractor _textExtractor;
    private readonly ISearchIndexer _searchIndexer;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateMetadataHandler"/> class.
    /// </summary>
    public UpdateMetadataHandler(
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
    /// <param name="command">The command describing the metadata updates.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated aggregate.</returns>
    public async Task<FileEntity> HandleAsync(UpdateMetadataCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var file = await _repository.GetAsync(command.FileId, cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            throw new InvalidOperationException($"File '{command.FileId}' was not found.");
        }

        if (command.Mime.HasValue || command.Author is not null)
        {
            file.UpdateMetadata(command.Mime, command.Author);
        }

        if (command.IsReadOnly.HasValue)
        {
            file.SetReadOnly(command.IsReadOnly.Value);
        }

        if (command.SystemMetadata.HasValue)
        {
            file.ApplySystemMetadata(command.SystemMetadata.Value);
        }

        ApplyExtendedMetadata(file, command.ExtendedMetadata);

        if (file.DomainEvents.Count == 0)
        {
            return file;
        }

        await FileIndexingHelper.ReindexAsync(file, _textExtractor, _searchIndexer, cancellationToken).ConfigureAwait(false);
        await _repository.UpdateAsync(file, cancellationToken).ConfigureAwait(false);
        await FileCommandHandlerHelpers.PublishAndClearAsync(_eventPublisher, file, cancellationToken).ConfigureAwait(false);
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
