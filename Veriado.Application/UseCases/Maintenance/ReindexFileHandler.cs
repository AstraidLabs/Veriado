namespace Veriado.Appl.UseCases.Maintenance;

/// <summary>
/// Handles explicit reindexing requests for a file.
/// </summary>
public sealed class ReindexFileHandler : FileWriteHandlerBase, IRequestHandler<ReindexFileCommand, AppResult<FileSummaryDto>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReindexFileHandler"/> class.
    /// </summary>
    public ReindexFileHandler(
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

    /// <inheritdoc />
    public async Task<AppResult<FileSummaryDto>> Handle(ReindexFileCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var file = await Repository.GetAsync(request.FileId, cancellationToken).ConfigureAwait(false);
            if (file is null)
            {
                return AppResult<FileSummaryDto>.NotFound($"File '{request.FileId}' was not found.");
            }

            var timestamp = CurrentTimestamp();
            file.RequestManualReindex(timestamp);
            await PersistAsync(file, FilePersistenceOptions.Default, cancellationToken).ConfigureAwait(false);
            return AppResult<FileSummaryDto>.Success(Mapper.Map<FileSummaryDto>(file));
        }
        catch (Exception ex)
        {
            return AppResult<FileSummaryDto>.FromException(ex, "Failed to reindex the file.");
        }
    }
}
