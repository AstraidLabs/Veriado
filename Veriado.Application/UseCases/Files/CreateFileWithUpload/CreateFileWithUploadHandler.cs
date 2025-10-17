namespace Veriado.Appl.UseCases.Files.CreateFileWithUpload;

/// <summary>
/// Handles creation of new file aggregates together with their backing file system content.
/// </summary>
public sealed class CreateFileWithUploadHandler : FileWriteHandlerBase, IRequestHandler<CreateFileWithUploadCommand, AppResult<Guid>>
{
    private readonly ImportPolicy _importPolicy;
    private readonly IFileStorage _fileStorage;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateFileWithUploadHandler"/> class.
    /// </summary>
    public CreateFileWithUploadHandler(
        IFileRepository repository,
        IClock clock,
        ImportPolicy importPolicy,
        IMapper mapper,
        IFileStorage fileStorage,
        DbContext dbContext,
        IFileSearchProjection searchProjection,
        ISearchIndexSignatureCalculator signatureCalculator)
        : base(repository, clock, mapper, dbContext, searchProjection, signatureCalculator)
    {
        _importPolicy = importPolicy ?? throw new ArgumentNullException(nameof(importPolicy));
        _fileStorage = fileStorage ?? throw new ArgumentNullException(nameof(fileStorage));
    }

    /// <inheritdoc />
    public async Task<AppResult<Guid>> Handle(CreateFileWithUploadCommand request, CancellationToken cancellationToken)
    {
        try
        {
            Guard.AgainstNull(request.Content, nameof(request.Content));
            _importPolicy.EnsureWithinLimit(request.Content.LongLength);

            var name = FileName.From(request.Name);
            var extension = FileExtension.From(request.Extension);
            var mime = MimeType.From(request.Mime);
            var size = ByteSize.From(request.Content.LongLength);
            var hash = FileHash.Compute(request.Content);

            if (await Repository.ExistsByHashAsync(hash, cancellationToken).ConfigureAwait(false))
            {
                return AppResult<Guid>.Conflict("A file with identical content already exists.");
            }

            await using var contentStream = new MemoryStream(request.Content, writable: false);
            var storageResult = await _fileStorage.SaveAsync(contentStream, cancellationToken).ConfigureAwait(false);
            storageResult = storageResult with { Mime = mime };

            if (storageResult.Hash != hash)
            {
                throw new InvalidOperationException("Stored content hash does not match the computed hash.");
            }

            if (storageResult.Size != size)
            {
                throw new InvalidOperationException("Stored content size does not match the provided payload.");
            }

            var createdAt = CurrentTimestamp();
            var fileSystem = FileSystemEntity.CreateNew(
                storageResult.Provider,
                storageResult.Path,
                storageResult.Hash,
                storageResult.Size,
                storageResult.Mime,
                storageResult.Attributes,
                storageResult.OwnerSid,
                storageResult.IsEncrypted,
                storageResult.CreatedUtc,
                storageResult.LastWriteUtc,
                storageResult.LastAccessUtc,
                createdAt);

            var file = FileEntity.CreateNew(
                name,
                extension,
                mime,
                request.Author,
                fileSystem.Id,
                storageResult.Hash,
                storageResult.Size,
                ContentVersion.Initial,
                createdAt);

            await PersistNewAsync(file, fileSystem, FilePersistenceOptions.Default, cancellationToken).ConfigureAwait(false);
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
        catch (Exception ex)
        {
            return AppResult<Guid>.FromException(ex, "Failed to create the file.");
        }
    }
}
