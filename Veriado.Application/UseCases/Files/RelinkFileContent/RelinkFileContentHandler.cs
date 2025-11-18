using Veriado.Appl.Abstractions;
using Veriado.Domain.ValueObjects;

namespace Veriado.Appl.UseCases.Files.RelinkFileContent;

/// <summary>
/// Handles relinking logical files to freshly uploaded file system content.
/// </summary>
public sealed class RelinkFileContentHandler : FileWriteHandlerBase, IRequestHandler<RelinkFileContentCommand, AppResult<FileSummaryDto>>
{
    private readonly ImportPolicy _importPolicy;
    private readonly IFileStorage _fileStorage;

    /// <summary>
    /// Initializes a new instance of the <see cref="RelinkFileContentHandler"/> class.
    /// </summary>
    public RelinkFileContentHandler(
        IFileRepository repository,
        IClock clock,
        ImportPolicy importPolicy,
        IMapper mapper,
        IFileStorage fileStorage,
        IFilePersistenceUnitOfWork unitOfWork,
        ISearchProjectionScope projectionScope,
        IFileSearchProjection searchProjection,
        ISearchIndexSignatureCalculator signatureCalculator)
        : base(repository, clock, mapper, unitOfWork, projectionScope, searchProjection, signatureCalculator)
    {
        _importPolicy = importPolicy ?? throw new ArgumentNullException(nameof(importPolicy));
        _fileStorage = fileStorage ?? throw new ArgumentNullException(nameof(fileStorage));
    }

    /// <inheritdoc />
    public async Task<AppResult<FileSummaryDto>> Handle(RelinkFileContentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            Guard.AgainstNull(request.Content, nameof(request.Content));
            _importPolicy.EnsureWithinLimit(request.Content.LongLength);

            var file = await Repository.GetAsync(request.FileId, cancellationToken).ConfigureAwait(false);
            if (file is null)
            {
                return AppResult<FileSummaryDto>.NotFound($"File '{request.FileId}' was not found.");
            }

            var newHash = FileHash.Compute(request.Content);
            if (newHash == file.ContentHash)
            {
                return AppResult<FileSummaryDto>.Success(Mapper.Map<FileSummaryDto>(file));
            }

            var newMime = MimeType.From(request.Mime);
            var newSize = ByteSize.From(request.Content.LongLength);

            await using var contentStream = new MemoryStream(request.Content, writable: false);
            var storageResult = await _fileStorage.SaveAsync(contentStream, cancellationToken).ConfigureAwait(false);
            storageResult = storageResult with { Mime = newMime };

            if (storageResult.Hash != newHash)
            {
                throw new InvalidOperationException("Stored content hash does not match the computed hash.");
            }

            if (storageResult.Size != newSize)
            {
                throw new InvalidOperationException("Stored content size does not match the provided payload.");
            }

            var fileSystem = await Repository.GetFileSystemAsync(file.FileSystemId, cancellationToken).ConfigureAwait(false);
            if (fileSystem is null)
            {
                throw new InvalidOperationException($"File system entity '{file.FileSystemId}' was not found.");
            }

            var relativePath = RelativeFilePath.From(storageResult.Path.Value);
            var timestamp = CurrentTimestamp();
            fileSystem.ReplaceContent(
                relativePath,
                storageResult.Hash,
                storageResult.Size,
                storageResult.Mime,
                storageResult.IsEncrypted,
                timestamp);
            fileSystem.UpdateAttributes(storageResult.Attributes, timestamp);
            fileSystem.UpdateOwner(storageResult.OwnerSid, timestamp);
            fileSystem.UpdateTimestamps(
                storageResult.CreatedUtc,
                storageResult.LastWriteUtc,
                storageResult.LastAccessUtc,
                timestamp);

            var link = FileContentLink.Create(
                storageResult.Provider.ToString(),
                storageResult.Path.Value,
                storageResult.Hash,
                storageResult.Size,
                fileSystem.ContentVersion,
                timestamp,
                storageResult.Mime);
            file.RelinkToExistingContent(link, DomainClock);

            await PersistAsync(file, fileSystem, FilePersistenceOptions.Default, cancellationToken).ConfigureAwait(false);
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
            return AppResult<FileSummaryDto>.FromException(ex, "Failed to relink file content.");
        }
    }
}
