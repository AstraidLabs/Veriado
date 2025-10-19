using Veriado.Appl.Abstractions;

namespace Veriado.Appl.UseCases.Maintenance;

/// <summary>
/// Handles bulk reindex operations over multiple files.
/// </summary>
public sealed class BulkReindexHandler : FileWriteHandlerBase, IRequestHandler<BulkReindexCommand, AppResult<int>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BulkReindexHandler"/> class.
    /// </summary>
    public BulkReindexHandler(
        IFileRepository repository,
        IClock clock,
        IMapper mapper,
        IFilePersistenceUnitOfWork unitOfWork,
        ISearchProjectionScope projectionScope,
        IFileSearchProjection searchProjection,
        ISearchIndexSignatureCalculator signatureCalculator)
        : base(repository, clock, mapper, unitOfWork, projectionScope, searchProjection, signatureCalculator)
    {
    }

    /// <inheritdoc />
    public async Task<AppResult<int>> Handle(BulkReindexCommand request, CancellationToken cancellationToken)
    {
        if (request.FileIds is null || request.FileIds.Count == 0)
        {
            return AppResult<int>.Validation(new[] { "At least one file identifier must be provided." });
        }

        var distinctIds = request.FileIds.Distinct().ToArray();
        var files = await Repository.GetManyAsync(distinctIds, cancellationToken).ConfigureAwait(false);
        if (files.Count != distinctIds.Length)
        {
            var foundIds = files.Select(f => f.Id).ToHashSet();
            var missing = distinctIds.Where(id => !foundIds.Contains(id)).Select(id => id.ToString());
            return AppResult<int>.NotFound($"Files not found: {string.Join(", ", missing)}");
        }

        foreach (var file in files)
        {
            var timestamp = CurrentTimestamp();
            file.RequestManualReindex(timestamp);
            await PersistAsync(file, FilePersistenceOptions.Default, cancellationToken).ConfigureAwait(false);
        }

        return AppResult<int>.Success(files.Count);
    }
}
