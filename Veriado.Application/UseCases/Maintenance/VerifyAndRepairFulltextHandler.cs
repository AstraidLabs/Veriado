using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Veriado.Application.Abstractions;
using Veriado.Application.Common;
using Veriado.Domain.Files;

namespace Veriado.Application.UseCases.Maintenance;

/// <summary>
/// Ensures that the search index accurately reflects the file corpus.
/// </summary>
public sealed class VerifyAndRepairFulltextHandler : IRequestHandler<VerifyAndRepairFulltextCommand, AppResult<int>>
{
    private readonly IFileRepository _repository;
    private readonly ISearchIndexCoordinator _indexCoordinator;
    private readonly IClock _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="VerifyAndRepairFulltextHandler"/> class.
    /// </summary>
    public VerifyAndRepairFulltextHandler(
        IFileRepository repository,
        ISearchIndexCoordinator indexCoordinator,
        IClock clock)
    {
        _repository = repository;
        _indexCoordinator = indexCoordinator;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<AppResult<int>> Handle(VerifyAndRepairFulltextCommand request, CancellationToken cancellationToken)
    {
        var repaired = 0;

        await foreach (var file in _repository.StreamAllAsync(cancellationToken))
        {
            if (!request.Force && !NeedsRepair(file))
            {
                continue;
            }

            var indexed = await _indexCoordinator.IndexAsync(file, request.ExtractContent, allowDeferred: false, cancellationToken)
                .ConfigureAwait(false);
            if (indexed)
            {
                file.ConfirmIndexed(file.SearchIndex.SchemaVersion, _clock.UtcNow);
            }
            await _repository.UpdateAsync(file, cancellationToken);
            repaired++;
        }

        return AppResult<int>.Success(repaired);
    }

    private static bool NeedsRepair(FileEntity file)
    {
        if (file.SearchIndex.IsStale)
        {
            return true;
        }

        var expectedHash = file.Content.Hash.Value;
        if (!string.Equals(file.SearchIndex.IndexedContentHash, expectedHash, StringComparison.Ordinal))
        {
            return true;
        }

        var expectedTitle = file.GetTitle() ?? file.Name.Value;
        if (!string.Equals(file.SearchIndex.IndexedTitle, expectedTitle, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
