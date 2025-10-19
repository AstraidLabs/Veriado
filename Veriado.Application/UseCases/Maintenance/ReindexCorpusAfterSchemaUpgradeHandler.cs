namespace Veriado.Appl.UseCases.Maintenance;

/// <summary>
/// Handles bulk reindex operations triggered by a schema upgrade.
/// </summary>
public sealed class ReindexCorpusAfterSchemaUpgradeHandler : FileWriteHandlerBase, IRequestHandler<ReindexCorpusAfterSchemaUpgradeCommand, AppResult<int>>
{
    public ReindexCorpusAfterSchemaUpgradeHandler(
        IFileRepository repository,
        IClock clock,
        IMapper mapper,
        IFilePersistenceUnitOfWork unitOfWork,
        IFileSearchProjection searchProjection,
        ISearchIndexSignatureCalculator signatureCalculator,
        ISearchProjectionScope projectionScope)
        : base(repository, clock, mapper, unitOfWork, searchProjection, signatureCalculator, projectionScope)
    {
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
            await foreach (var file in Repository.StreamAllAsync(cancellationToken))
            {
                var timestamp = CurrentTimestamp();
                file.BumpSchemaVersion(request.TargetSchemaVersion, timestamp);
                await PersistAsync(file, FilePersistenceOptions.Default, cancellationToken).ConfigureAwait(false);
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
