namespace Veriado.Appl.UseCases.Files.ClearFileValidity;

/// <summary>
/// Handles clearing document validity from files.
/// </summary>
public sealed class ClearFileValidityHandler : FileWriteHandlerBase, IRequestHandler<ClearFileValidityCommand, AppResult<FileSummaryDto>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ClearFileValidityHandler"/> class.
    /// </summary>
    public ClearFileValidityHandler(
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
    public async Task<AppResult<FileSummaryDto>> Handle(ClearFileValidityCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var file = await Repository.GetAsync(request.FileId, cancellationToken);
            if (file is null)
            {
                return AppResult<FileSummaryDto>.NotFound($"File '{request.FileId}' was not found.");
            }

            var timestamp = CurrentTimestamp();
            file.ClearValidity(timestamp);
            await PersistAsync(file, FilePersistenceOptions.Default, cancellationToken);
            return AppResult<FileSummaryDto>.Success(Mapper.Map<FileSummaryDto>(file));
        }
        catch (InvalidOperationException ex)
        {
            return AppResult<FileSummaryDto>.FromException(ex);
        }
        catch (Exception ex)
        {
            return AppResult<FileSummaryDto>.FromException(ex, "Failed to clear file validity.");
        }
    }

}
