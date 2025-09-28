namespace Veriado.Appl.UseCases.Maintenance;

/// <summary>
/// Handles bulk reindex operations over multiple files.
/// </summary>
public sealed class BulkReindexHandler : IRequestHandler<BulkReindexCommand, AppResult<int>>
{
    private readonly IFileRepository _repository;
    private readonly IClock _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="BulkReindexHandler"/> class.
    /// </summary>
    public BulkReindexHandler(IFileRepository repository, IClock clock)
    {
        _repository = repository;
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

        var timestamp = UtcTimestamp.From(_clock.UtcNow);
        foreach (var file in files)
        {
            file.RequestManualReindex(timestamp);
            await _repository.UpdateAsync(file, FilePersistenceOptions.Default, cancellationToken).ConfigureAwait(false);
        }

        return AppResult<int>.Success(files.Count);
    }
}
