using Veriado.Appl.Abstractions;
using Veriado.Appl.Common.Exceptions;

namespace Veriado.Appl.UseCases.Files.Common;

/// <summary>
/// Provides reusable persistence helpers for file write handlers.
/// </summary>
public abstract class FileWriteHandlerBase
{
    private readonly IFileRepository _repository;
    private readonly IClock _clock;
    private readonly IFilePersistenceUnitOfWork _unitOfWork;
    private readonly ISearchProjectionScope _projectionScope;
    private readonly IFileSearchProjection _searchProjection;
    private readonly ISearchIndexSignatureCalculator _signatureCalculator;

    protected IMapper Mapper { get; }

    protected Veriado.Domain.Primitives.IClock DomainClock => new ClockAdapter(_clock);

    protected IFileRepository Repository => _repository;

    protected FileWriteHandlerBase(
        IFileRepository repository,
        IClock clock,
        IMapper mapper,
        IFilePersistenceUnitOfWork unitOfWork,
        ISearchProjectionScope projectionScope,
        IFileSearchProjection searchProjection,
        ISearchIndexSignatureCalculator signatureCalculator)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _projectionScope = projectionScope ?? throw new ArgumentNullException(nameof(projectionScope));
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

    protected async Task DeleteAsync(FileEntity file, FileSystemEntity? fileSystem, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        await _repository.DeleteAsync(file.Id, cancellationToken).ConfigureAwait(false);
        if (fileSystem is not null)
        {
            await _repository.DeleteFileSystemAsync(fileSystem.Id, cancellationToken).ConfigureAwait(false);
        }

        await CommitAsync(file, fileSystem: null, requiresProjection: true, deleteFromProjection: true, cancellationToken)
            .ConfigureAwait(false);
    }

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
            if (!await _unitOfWork.HasTrackedChangesAsync(cancellationToken).ConfigureAwait(false)
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
        await using var transaction = await _unitOfWork
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (requiresProjection)
        {
            _projectionScope.EnsureActive();

            await _projectionScope
                .ExecuteAsync(
                    async ct =>
                    {
                        if (deleteFromProjection)
                        {
                            await _searchProjection.DeleteAsync(file.Id, _projectionScope, ct).ConfigureAwait(false);
                            return;
                        }

                        var signature = _signatureCalculator.Compute(file);
                        var searchState = file.SearchIndex;
                        var expectedContentHash = searchState?.IndexedContentHash;
                        var expectedTokenHash = searchState?.TokenHash;
                        var newContentHash = file.ContentHash.Value;
                        var newTokenHash = signature.TokenHash;

                        var projectionPerformed = false;

                        try
                        {
                            projectionPerformed = await _searchProjection
                                .UpsertAsync(
                                    file,
                                    expectedContentHash,
                                    expectedTokenHash,
                                    newContentHash,
                                    newTokenHash,
                                    _projectionScope,
                                    ct)
                                .ConfigureAwait(false);
                        }
                        catch (AnalyzerOrContentDriftException)
                        {
                            projectionPerformed = await _searchProjection
                                .ForceReplaceAsync(
                                    file,
                                    newContentHash,
                                    newTokenHash,
                                    _projectionScope,
                                    ct)
                                .ConfigureAwait(false);
                        }

                        if (!projectionPerformed)
                        {
                            return;
                        }

                        var schemaVersion = searchState?.SchemaVersion ?? 1;
                        file.ConfirmIndexed(
                            schemaVersion,
                            CurrentTimestamp(),
                            signature.AnalyzerVersion,
                            newTokenHash,
                            signature.NormalizedTitle);
                        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
    private sealed class ClockAdapter : Veriado.Domain.Primitives.IClock
    {
        private readonly IClock _inner;

        public ClockAdapter(IClock inner)
        {
            _inner = inner;
        }

        public DateTimeOffset UtcNow => _inner.UtcNow;
    }
}
