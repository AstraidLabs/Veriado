using Veriado.Appl.Common.Exceptions;

namespace Veriado.Appl.UseCases.Files.CreateFile;

/// <summary>
/// Handles creation of new file aggregates.
/// </summary>
public sealed class CreateFileHandler : FileWriteHandlerBase, IRequestHandler<CreateFileCommand, AppResult<Guid>>
{
    private readonly ImportPolicy _importPolicy;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateFileHandler"/> class.
    /// </summary>
    public CreateFileHandler(
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
    public async Task<AppResult<Guid>> Handle(CreateFileCommand request, CancellationToken cancellationToken)
    {
        try
        {
            Guard.AgainstNull(request.Content, nameof(request.Content));
            _importPolicy.EnsureWithinLimit(request.Content.LongLength);

            var name = FileName.From(request.Name);
            var extension = FileExtension.From(request.Extension);
            var mime = MimeType.From(request.Mime);
            var createdAt = CurrentTimestamp();
            var size = ByteSize.From(request.Content.LongLength);
            var hash = FileHash.Compute(request.Content);
            var file = FileEntity.CreateNew(
                name,
                extension,
                mime,
                request.Author,
                Guid.NewGuid(),
                hash,
                size,
                ContentVersion.Initial,
                createdAt);

            if (await Repository.ExistsByHashAsync(file.ContentHash, cancellationToken).ConfigureAwait(false))
            {
                return AppResult<Guid>.Conflict("A file with identical content already exists.");
            }

            await PersistNewAsync(file, FilePersistenceOptions.Default, cancellationToken);
            return AppResult<Guid>.Success(file.Id);
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            return AppResult<Guid>.FromException(ex);
        }
        catch (InvalidOperationException ex)
        {
            return AppResult<Guid>.FromException(ex);
        }
        catch (DuplicateFileContentException)
        {
            return AppResult<Guid>.Conflict("A file with identical content already exists.");
        }
        catch (Exception ex)
        {
            return AppResult<Guid>.FromException(ex, "Failed to create the file.");
        }
    }
}
