using System.IO;
using MediatR;
using Veriado.Appl.Abstractions;
using Veriado.Appl.Common;
using Veriado.Appl.Common.Exceptions;
using Veriado.Appl.UseCases.Files.Common;
using Veriado.Contracts.Files;
using Veriado.Domain.Files;
using Veriado.Domain.FileSystem;
using Veriado.Domain.ValueObjects;

namespace Veriado.Appl.UseCases.Files.UpdateFileContent;

/// <summary>
/// Handles updating physical file content from a user-specified path.
/// </summary>
public sealed class UpdateFileContentCommandHandler
    : FileWriteHandlerBase,
        IRequestHandler<UpdateFileContentCommand, AppResult<FileSummaryDto>>
{
    private const string DefaultMimeType = "application/octet-stream";

    private readonly ImportPolicy _importPolicy;
    private readonly IFilePathResolver _pathResolver;
    private readonly IFileStorage _fileStorage;
    private readonly IFileHashCalculator _hashCalculator;

    public UpdateFileContentCommandHandler(
        IFileRepository repository,
        IClock clock,
        ImportPolicy importPolicy,
        IMapper mapper,
        IFilePersistenceUnitOfWork unitOfWork,
        ISearchProjectionScope projectionScope,
        IFileSearchProjection searchProjection,
        ISearchIndexSignatureCalculator signatureCalculator,
        IFilePathResolver pathResolver,
        IFileStorage fileStorage,
        IFileHashCalculator hashCalculator)
        : base(repository, clock, mapper, unitOfWork, projectionScope, searchProjection, signatureCalculator)
    {
        _importPolicy = importPolicy ?? throw new ArgumentNullException(nameof(importPolicy));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _fileStorage = fileStorage ?? throw new ArgumentNullException(nameof(fileStorage));
        _hashCalculator = hashCalculator ?? throw new ArgumentNullException(nameof(hashCalculator));
    }

    public async Task<AppResult<FileSummaryDto>> Handle(
        UpdateFileContentCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceFileFullPath);

            var sourceFullPath = Path.GetFullPath(request.SourceFileFullPath);
            var sourceInfo = new FileInfo(sourceFullPath);
            if (!sourceInfo.Exists)
            {
                return AppResult<FileSummaryDto>.NotFound(
                    $"Source file '{request.SourceFileFullPath}' was not found.");
            }

            _importPolicy.EnsureWithinLimit(sourceInfo.Length);

            var file = await Repository.GetAsync(request.FileId, cancellationToken).ConfigureAwait(false);
            if (file is null)
            {
                return AppResult<FileSummaryDto>.NotFound($"File '{request.FileId}' was not found.");
            }

            var fileSystem = await Repository
                .GetFileSystemAsync(file.FileSystemId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException($"File system entity '{file.FileSystemId}' was not found.");

            var storageResult = await ResolveStorageAsync(sourceFullPath, cancellationToken).ConfigureAwait(false);
            var relativePath = RelativeFilePath.From(storageResult.Path.Value);
            var mime = string.Equals(storageResult.Mime.Value, DefaultMimeType, StringComparison.OrdinalIgnoreCase)
                ? file.Mime
                : storageResult.Mime;

            var timestamp = CurrentTimestamp();
            fileSystem.ReplaceContent(
                relativePath,
                storageResult.Hash,
                storageResult.Size,
                mime,
                storageResult.IsEncrypted,
                timestamp);
            fileSystem.UpdateAttributes(storageResult.Attributes, timestamp);
            fileSystem.UpdateOwner(storageResult.OwnerSid, timestamp);
            fileSystem.UpdateTimestamps(
                storageResult.CreatedUtc,
                storageResult.LastWriteUtc,
                storageResult.LastAccessUtc,
                timestamp);
            fileSystem.MarkHealthy();

            var link = FileContentLink.Create(
                storageResult.Provider.ToString(),
                storageResult.Path.Value,
                storageResult.Hash,
                storageResult.Size,
                fileSystem.ContentVersion,
                timestamp,
                mime);
            file.RelinkToExistingContent(link, DomainClock);

            await PersistAsync(file, fileSystem, FilePersistenceOptions.Default, cancellationToken)
                .ConfigureAwait(false);
            return AppResult<FileSummaryDto>.Success(Mapper.Map<FileSummaryDto>(file));
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or FileNotFoundException)
        {
            return AppResult<FileSummaryDto>.FromException(ex);
        }
        catch (InvalidOperationException ex)
        {
            return AppResult<FileSummaryDto>.FromException(ex);
        }
        catch (Exception ex)
        {
            return AppResult<FileSummaryDto>.FromException(ex, "Failed to update file content.");
        }
    }

    private async Task<StorageResult> ResolveStorageAsync(
        string sourceFullPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var relative = _pathResolver.GetRelativePath(sourceFullPath);
            var stat = await _fileStorage
                .StatAsync(StoragePath.From(relative), cancellationToken)
                .ConfigureAwait(false);
            return stat.ToStorageResult();
        }
        catch (InvalidOperationException)
        {
            // Source file is not under the configured storage root; fall back to copying.
        }

        var expectedHash = await _hashCalculator
            .ComputeSha256Async(sourceFullPath, cancellationToken)
            .ConfigureAwait(false);
        await using var stream = new FileStream(
            sourceFullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        var storageResult = await _fileStorage.SaveAsync(stream, cancellationToken).ConfigureAwait(false);

        if (storageResult.Hash != expectedHash)
        {
            throw new InvalidOperationException("Stored content hash does not match the source file hash.");
        }

        return storageResult;
    }
}
