namespace Veriado.Appl.UseCases.Files.SetFileValidity;

/// <summary>
/// Handles updates to document validity metadata for files.
/// </summary>
public sealed class SetFileValidityHandler : FileWriteHandlerBase, IRequestHandler<SetFileValidityCommand, AppResult<FileSummaryDto>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SetFileValidityHandler"/> class.
    /// </summary>
    public SetFileValidityHandler(IFileRepository repository, IClock clock, IMapper mapper)
        : base(repository, clock, mapper)
    {
    }

    /// <inheritdoc />
    public async Task<AppResult<FileSummaryDto>> Handle(SetFileValidityCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var file = await Repository.GetAsync(request.FileId, cancellationToken);
            if (file is null)
            {
                return AppResult<FileSummaryDto>.NotFound($"File '{request.FileId}' was not found.");
            }

            var issued = UtcTimestamp.From(request.IssuedAtUtc);
            var validUntil = UtcTimestamp.From(request.ValidUntilUtc);
            var timestamp = CurrentTimestamp();
            file.SetValidity(issued, validUntil, request.HasPhysicalCopy, request.HasElectronicCopy, timestamp);
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
            return AppResult<FileSummaryDto>.FromException(ex, "Failed to update file validity.");
        }
    }

}
