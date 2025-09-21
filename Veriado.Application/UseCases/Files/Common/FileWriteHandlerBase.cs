using System.Threading;
using System.Threading.Tasks;
using Veriado.Application.Abstractions;
using Veriado.Domain.Files;

namespace Veriado.Application.UseCases.Files.Common;

/// <summary>
/// Provides reusable persistence helpers for file write handlers.
/// </summary>
public abstract class FileWriteHandlerBase
{
    private readonly IFileRepository _repository;

    protected IFileRepository Repository => _repository;

    protected FileWriteHandlerBase(IFileRepository repository)
    {
        _repository = repository;
    }

    protected Task PersistNewAsync(FileEntity file, CancellationToken cancellationToken)
        => PersistInternalAsync(file, addFirst: true, cancellationToken);

    protected Task PersistNewAsync(FileEntity file, bool extractContent, CancellationToken cancellationToken)
        => PersistInternalAsync(file, addFirst: true, cancellationToken);

    protected Task PersistAsync(FileEntity file, CancellationToken cancellationToken)
        => PersistInternalAsync(file, addFirst: false, cancellationToken);

    protected Task PersistAsync(FileEntity file, bool extractContent, CancellationToken cancellationToken)
        => PersistInternalAsync(file, addFirst: false, cancellationToken);

    private Task PersistInternalAsync(
        FileEntity file,
        bool addFirst,
        CancellationToken cancellationToken)
    {
        if (addFirst)
        {
            return _repository.AddAsync(file, cancellationToken);
        }

        if (file.DomainEvents.Count == 0 && !file.SearchIndex.IsStale)
        {
            return Task.CompletedTask;
        }

        return _repository.UpdateAsync(file, cancellationToken);
    }
}
