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
        DbContext dbContext,
        IFileSearchProjection searchProjection,
        ISearchIndexSignatureCalculator signatureCalculator)
        : base(repository, clock, mapper, dbContext, searchProjection, signatureCalculator)
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
