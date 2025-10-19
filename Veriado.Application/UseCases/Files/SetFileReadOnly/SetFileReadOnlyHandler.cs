namespace Veriado.Appl.UseCases.Files.SetFileReadOnly;

/// <summary>
/// Handles toggling the read-only status of a file aggregate.
/// </summary>
public sealed class SetFileReadOnlyHandler : FileWriteHandlerBase, IRequestHandler<SetFileReadOnlyCommand, AppResult<FileSummaryDto>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SetFileReadOnlyHandler"/> class.
    /// </summary>
    public SetFileReadOnlyHandler(
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
    public async Task<AppResult<FileSummaryDto>> Handle(SetFileReadOnlyCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var file = await Repository.GetAsync(request.FileId, cancellationToken);
            if (file is null)
            {
                return AppResult<FileSummaryDto>.NotFound($"File '{request.FileId}' was not found.");
            }

            var timestamp = CurrentTimestamp();
            file.SetReadOnly(request.IsReadOnly, timestamp);
            await PersistAsync(file, FilePersistenceOptions.Default, cancellationToken);
            return AppResult<FileSummaryDto>.Success(Mapper.Map<FileSummaryDto>(file));
        }
        catch (InvalidOperationException ex)
        {
            return AppResult<FileSummaryDto>.FromException(ex);
        }
        catch (Exception ex)
        {
            return AppResult<FileSummaryDto>.FromException(ex, "Failed to change the read-only status.");
        }
    }

}
