namespace Veriado.Appl.UseCases.Maintenance;

/// <summary>
/// Handles bulk reindex operations triggered by a schema upgrade.
/// </summary>
public sealed class ReindexCorpusAfterSchemaUpgradeHandler : IRequestHandler<ReindexCorpusAfterSchemaUpgradeCommand, AppResult<int>>
{
    private readonly IFileRepository _repository;
    private readonly IClock _clock;

    public ReindexCorpusAfterSchemaUpgradeHandler(IFileRepository repository, IClock clock)
    {
        _repository = repository;
        _clock = clock;
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
            var options = new FilePersistenceOptions
            {
                AllowDeferredIndexing = request.AllowDeferredIndexing,
            };

            await foreach (var file in _repository.StreamAllAsync(cancellationToken))
            {
                var timestamp = UtcTimestamp.From(_clock.UtcNow);
                file.BumpSchemaVersion(request.TargetSchemaVersion, timestamp);
                await _repository.UpdateAsync(file, options, cancellationToken).ConfigureAwait(false);
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
