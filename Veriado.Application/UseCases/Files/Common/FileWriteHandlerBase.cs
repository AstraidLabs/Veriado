using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Veriado.Application.Abstractions;
using Veriado.Domain.Files;
using Veriado.Domain.ValueObjects;

namespace Veriado.Application.UseCases.Files.Common;

/// <summary>
/// Provides reusable persistence helpers for file write handlers.
/// </summary>
public abstract class FileWriteHandlerBase
{
    private readonly IFileRepository _repository;
    private readonly IClock _clock;

    protected IMapper Mapper { get; }

    protected IFileRepository Repository => _repository;

    protected FileWriteHandlerBase(IFileRepository repository, IClock clock, IMapper mapper)
    {
        _repository = repository;
        _clock = clock;
        Mapper = mapper;
    }

    protected UtcTimestamp CurrentTimestamp() => UtcTimestamp.From(_clock.UtcNow);

    protected Task PersistNewAsync(FileEntity file, FilePersistenceOptions options, CancellationToken cancellationToken)
        => PersistInternalAsync(file, addFirst: true, options, cancellationToken);

    protected Task PersistAsync(FileEntity file, FilePersistenceOptions options, CancellationToken cancellationToken)
        => PersistInternalAsync(file, addFirst: false, options, cancellationToken);

    private Task PersistInternalAsync(
        FileEntity file,
        bool addFirst,
        FilePersistenceOptions options,
        CancellationToken cancellationToken)
    {
        if (addFirst)
        {
            return _repository.AddAsync(file, options, cancellationToken);
        }

        if (file.DomainEvents.Count == 0 && !file.SearchIndex.IsStale)
        {
            return Task.CompletedTask;
        }

        return _repository.UpdateAsync(file, options, cancellationToken);
    }
}
