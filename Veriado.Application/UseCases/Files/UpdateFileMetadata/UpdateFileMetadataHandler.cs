namespace Veriado.Appl.UseCases.Files.UpdateFileMetadata;

/// <summary>
/// Handles updates of core file metadata.
/// </summary>
public sealed class UpdateFileMetadataHandler : FileWriteHandlerBase, IRequestHandler<UpdateFileMetadataCommand, AppResult<FileSummaryDto>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateFileMetadataHandler"/> class.
    /// </summary>
    public UpdateFileMetadataHandler(IFileRepository repository, IClock clock, IMapper mapper)
        : base(repository, clock, mapper)
    {
    }

    /// <inheritdoc />
    public async Task<AppResult<FileSummaryDto>> Handle(UpdateFileMetadataCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var file = await Repository.GetAsync(request.FileId, cancellationToken);
            if (file is null)
            {
                return AppResult<FileSummaryDto>.NotFound($"File '{request.FileId}' was not found.");
            }

            MimeType? mime = request.Mime is null ? null : MimeType.From(request.Mime);
            var timestamp = CurrentTimestamp();
            file.UpdateMetadata(mime, request.Author, timestamp);
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
            return AppResult<FileSummaryDto>.FromException(ex, "Failed to update file metadata.");
        }
    }

}
