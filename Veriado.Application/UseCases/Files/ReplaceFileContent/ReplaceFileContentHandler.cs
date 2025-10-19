using Veriado.Domain.ValueObjects;

namespace Veriado.Appl.UseCases.Files.ReplaceFileContent;

/// <summary>
/// Handles replacing file content while ensuring search index synchronization.
/// </summary>
public sealed class ReplaceFileContentHandler : FileWriteHandlerBase, IRequestHandler<ReplaceFileContentCommand, AppResult<FileSummaryDto>>
{
    private readonly ImportPolicy _importPolicy;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplaceFileContentHandler"/> class.
    /// </summary>
    public ReplaceFileContentHandler(
        IFileRepository repository,
        IClock clock,
        ImportPolicy importPolicy,
        IMapper mapper,
        IFilePersistenceUnitOfWork unitOfWork,
        IFileSearchProjection searchProjection,
        ISearchIndexSignatureCalculator signatureCalculator,
        ISearchProjectionScope projectionScope)
        : base(repository, clock, mapper, unitOfWork, searchProjection, signatureCalculator, projectionScope)
    {
        _importPolicy = importPolicy;
    }

    /// <inheritdoc />
    public async Task<AppResult<FileSummaryDto>> Handle(ReplaceFileContentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            Guard.AgainstNull(request.Content, nameof(request.Content));
            _importPolicy.EnsureWithinLimit(request.Content.LongLength);

            var file = await Repository.GetAsync(request.FileId, cancellationToken);
            if (file is null)
            {
                return AppResult<FileSummaryDto>.NotFound($"File '{request.FileId}' was not found.");
            }

            var timestamp = CurrentTimestamp();
            var newHash = FileHash.Compute(request.Content);
            if (newHash == file.ContentHash)
            {
                return AppResult<FileSummaryDto>.Success(Mapper.Map<FileSummaryDto>(file));
            }

            var newSize = ByteSize.From(request.Content.LongLength);
            var nextVersion = file.LinkedContentVersion.Next();
            file.LinkTo(Guid.NewGuid(), newHash, newSize, nextVersion, file.Mime, timestamp);
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
            return AppResult<FileSummaryDto>.FromException(ex, "Failed to replace file content.");
        }
    }

}
