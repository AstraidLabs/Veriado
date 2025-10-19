using Veriado.Appl.Abstractions;

namespace Veriado.Appl.UseCases.Files.RenameFile;

/// <summary>
/// Handles renaming file aggregates.
/// </summary>
public sealed class RenameFileHandler : FileWriteHandlerBase, IRequestHandler<RenameFileCommand, AppResult<FileSummaryDto>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RenameFileHandler"/> class.
    /// </summary>
    public RenameFileHandler(
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
    public async Task<AppResult<FileSummaryDto>> Handle(RenameFileCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var file = await Repository.GetAsync(request.FileId, cancellationToken);
            if (file is null)
            {
                return AppResult<FileSummaryDto>.NotFound($"File '{request.FileId}' was not found.");
            }

            var newName = FileName.From(request.Name);
            var timestamp = CurrentTimestamp();
            file.Rename(newName, timestamp);
            await PersistAsync(file, FilePersistenceOptions.Default, cancellationToken);
            return AppResult<FileSummaryDto>.Success(Mapper.Map<FileSummaryDto>(file));
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            return AppResult<FileSummaryDto>.FromException(ex);
        }
        catch (InvalidOperationException ex)
        {
            return AppResult<FileSummaryDto>.FromException(ex);
        }
        catch (Exception ex)
        {
            return AppResult<FileSummaryDto>.FromException(ex, "Failed to rename file.");
        }
    }

}
