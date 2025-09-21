using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Veriado.Application.Abstractions;
using Veriado.Application.Common;
using Veriado.Domain.Files;

namespace Veriado.Application.UseCases.Maintenance;

/// <summary>
/// Handles bulk reindex operations triggered by a schema upgrade.
/// </summary>
public sealed class ReindexCorpusAfterSchemaUpgradeHandler : IRequestHandler<ReindexCorpusAfterSchemaUpgradeCommand, AppResult<int>>
{
    private readonly IFileRepository _repository;

    public ReindexCorpusAfterSchemaUpgradeHandler(IFileRepository repository)
    {
        _repository = repository;
    }

    public async Task<AppResult<int>> Handle(ReindexCorpusAfterSchemaUpgradeCommand request, CancellationToken cancellationToken)
    {
        if (request.TargetSchemaVersion <= 0)
        {
            return AppResult<int>.Validation(new[] { "Target schema version must be positive." });
        }

        var reindexed = 0;

        try
        {
            await foreach (var file in _repository.StreamAllAsync(cancellationToken))
            {
                file.BumpSchemaVersion(request.TargetSchemaVersion);
                await _repository.UpdateAsync(file, cancellationToken).ConfigureAwait(false);
                reindexed++;
            }

            return AppResult<int>.Success(reindexed);
        }
        catch (Exception ex)
        {
            return AppResult<int>.FromException(ex, "Failed to reindex the corpus after schema upgrade.");
        }
    }
}
