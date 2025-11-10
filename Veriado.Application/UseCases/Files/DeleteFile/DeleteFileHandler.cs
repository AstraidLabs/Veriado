using Veriado.Appl.Abstractions;

namespace Veriado.Appl.UseCases.Files.DeleteFile;

/// <summary>
/// Handles removal of file aggregates.
/// </summary>
public sealed class DeleteFileHandler : FileWriteHandlerBase, IRequestHandler<DeleteFileCommand, AppResult<Guid>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteFileHandler"/> class.
    /// </summary>
    public DeleteFileHandler(
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
    public async Task<AppResult<Guid>> Handle(DeleteFileCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var file = await Repository.GetAsync(request.FileId, cancellationToken).ConfigureAwait(false);
            if (file is null)
            {
                return AppResult<Guid>.NotFound($"File '{request.FileId}' was not found.");
            }

            var fileSystem = await Repository
                .GetFileSystemAsync(file.FileSystemId, cancellationToken)
                .ConfigureAwait(false);
            if (fileSystem is null)
            {
                throw new InvalidOperationException($"File system entity '{file.FileSystemId}' was not found.");
            }

            await DeleteAsync(file, fileSystem, cancellationToken).ConfigureAwait(false);
            return AppResult<Guid>.Success(request.FileId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return AppResult<Guid>.FromException(ex, "Failed to delete file.");
        }
    }
}
