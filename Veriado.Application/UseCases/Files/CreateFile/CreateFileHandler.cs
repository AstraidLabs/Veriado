using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Veriado.Domain.ValueObjects;

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
        DbContext dbContext,
        IFileSearchProjection searchProjection,
        ISearchIndexSignatureCalculator signatureCalculator)
        : base(repository, clock, mapper, dbContext, searchProjection, signatureCalculator)
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
        catch (DbUpdateException ex) when (IsDuplicateContentHashViolation(ex))
        {
            return AppResult<Guid>.Conflict("A file with identical content already exists.");
        }
        catch (Exception ex)
        {
            return AppResult<Guid>.FromException(ex, "Failed to create the file.");
        }
    }

    private static bool IsDuplicateContentHashViolation(DbUpdateException exception)
    {
        if (exception.InnerException is not SqliteException sqlite)
        {
            return false;
        }

        const int SqliteConstraint = 19; // SQLITE_CONSTRAINT
        const int SqliteConstraintUnique = 2067; // SQLITE_CONSTRAINT_UNIQUE

        if (sqlite.SqliteErrorCode != SqliteConstraint)
        {
            return false;
        }

        if (sqlite.SqliteExtendedErrorCode != 0 && sqlite.SqliteExtendedErrorCode != SqliteConstraintUnique)
        {
            return false;
        }

        return sqlite.Message.Contains("files.content_hash", StringComparison.OrdinalIgnoreCase)
            || sqlite.Message.Contains("files_content.hash", StringComparison.OrdinalIgnoreCase)
            || sqlite.Message.Contains("ux_files_content_hash", StringComparison.OrdinalIgnoreCase);
    }
}
