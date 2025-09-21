using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Veriado.Application.Abstractions;
using Veriado.Application.Common;
using Veriado.Domain.Files;

namespace Veriado.Application.UseCases.Maintenance;

/// <summary>
/// Handles bulk reindex operations over multiple files.
/// </summary>
public sealed class BulkReindexHandler : IRequestHandler<BulkReindexCommand, AppResult<int>>
{
    private readonly IFileRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ISearchIndexCoordinator _indexCoordinator;
    private readonly IClock _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="BulkReindexHandler"/> class.
    /// </summary>
    public BulkReindexHandler(
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
    public async Task<AppResult<int>> Handle(BulkReindexCommand request, CancellationToken cancellationToken)
    {
        if (request.FileIds is null || request.FileIds.Count == 0)
        {
            return AppResult<int>.Validation(new[] { "At least one file identifier must be provided." });
        }

        var distinctIds = request.FileIds.Distinct().ToArray();
        var files = await _repository.GetManyAsync(distinctIds, cancellationToken);
        if (files.Count != distinctIds.Length)
        {
            var foundIds = files.Select(f => f.Id).ToHashSet();
            var missing = distinctIds.Where(id => !foundIds.Contains(id)).Select(id => id.ToString());
            return AppResult<int>.NotFound($"Files not found: {string.Join(", ", missing)}");
        }

        foreach (var file in files)
        {
            await PublishDomainEventsAsync(file, cancellationToken);
            var indexed = await _indexCoordinator.IndexAsync(file, request.ExtractContent, allowDeferred: false, cancellationToken)
                .ConfigureAwait(false);
            if (indexed)
            {
                file.ConfirmIndexed(file.SearchIndex.SchemaVersion, _clock.UtcNow);
            }
            await _repository.UpdateAsync(file, cancellationToken);
        }

        return AppResult<int>.Success(files.Count);
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
