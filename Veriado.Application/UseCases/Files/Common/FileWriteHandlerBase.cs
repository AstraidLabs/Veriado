using Microsoft.EntityFrameworkCore;

namespace Veriado.Appl.UseCases.Files.Common;

/// <summary>
/// Provides reusable persistence helpers for file write handlers.
/// </summary>
public abstract class FileWriteHandlerBase
{
    private readonly IFileRepository _repository;
    private readonly IClock _clock;
    private readonly DbContext _dbContext;
    private readonly IFileSearchProjection _searchProjection;
    private readonly ISearchIndexSignatureCalculator _signatureCalculator;

    protected IMapper Mapper { get; }

    protected IFileRepository Repository => _repository;

    protected FileWriteHandlerBase(
        IFileRepository repository,
        IClock clock,
        IMapper mapper,
        DbContext dbContext,
        IFileSearchProjection searchProjection,
        ISearchIndexSignatureCalculator signatureCalculator)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _searchProjection = searchProjection ?? throw new ArgumentNullException(nameof(searchProjection));
        _signatureCalculator = signatureCalculator ?? throw new ArgumentNullException(nameof(signatureCalculator));
    }

    protected UtcTimestamp CurrentTimestamp() => UtcTimestamp.From(_clock.UtcNow);

    protected Task PersistNewAsync(FileEntity file, FilePersistenceOptions options, CancellationToken cancellationToken)
        => PersistInternalAsync(file, fileSystem: null, addFirst: true, options, cancellationToken);

    protected Task PersistNewAsync(
        FileEntity file,
        FileSystemEntity fileSystem,
        FilePersistenceOptions options,
        CancellationToken cancellationToken)
        => PersistInternalAsync(file, fileSystem, addFirst: true, options, cancellationToken);

    protected Task PersistAsync(FileEntity file, FilePersistenceOptions options, CancellationToken cancellationToken)
        => PersistInternalAsync(file, fileSystem: null, addFirst: false, options, cancellationToken);

    protected Task PersistAsync(
        FileEntity file,
        FileSystemEntity fileSystem,
        FilePersistenceOptions options,
        CancellationToken cancellationToken)
        => PersistInternalAsync(file, fileSystem, addFirst: false, options, cancellationToken);

    private async Task PersistInternalAsync(
        FileEntity file,
        FileSystemEntity? fileSystem,
        bool addFirst,
        FilePersistenceOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        _ = options;

        if (addFirst)
        {
            if (fileSystem is null)
            {
                await _repository.AddAsync(file, options, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _repository
                    .AddAsync(file, fileSystem, options, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        else
        {
            if (!_dbContext.ChangeTracker.HasChanges()
                && file.DomainEvents.Count == 0
                && (fileSystem?.DomainEvents.Count ?? 0) == 0
                && !file.SearchIndex.IsStale)
            {
                return;
            }

            if (fileSystem is null)
            {
                await _repository.UpdateAsync(file, options, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _repository
                    .UpdateAsync(file, fileSystem, options, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var requiresProjection = file.SearchIndex?.IsStale ?? false;
        await CommitAsync(file, fileSystem, requiresProjection, deleteFromProjection: false, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task CommitAsync(
        FileEntity file,
        FileSystemEntity? fileSystem,
        bool requiresProjection,
        bool deleteFromProjection,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (requiresProjection)
        {
            if (deleteFromProjection)
            {
                await _searchProjection.DeleteAsync(file.Id, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _searchProjection.UpsertAsync(file, cancellationToken).ConfigureAwait(false);
                var signature = _signatureCalculator.Compute(file);
                file.ConfirmIndexed(
                    file.SearchIndex.SchemaVersion,
                    CurrentTimestamp(),
                    signature.AnalyzerVersion,
                    signature.TokenHash,
                    signature.NormalizedTitle);
                await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await _repository.PersistDomainEventsAsync(file, fileSystem, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
