using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Veriado.Appl.Abstractions;
using Veriado.Domain.ValueObjects;

namespace Veriado.Appl.UseCases.Files.CheckFileHash;

/// <summary>
/// Represents a query that determines whether a file with a specific content hash already exists.
/// </summary>
/// <param name="Hash">The SHA-256 hash in hexadecimal representation.</param>
public sealed record FileHashExistsQuery(string Hash) : IRequest<bool>;

/// <summary>
/// Handles <see cref="FileHashExistsQuery"/> instances.
/// </summary>
public sealed class FileHashExistsQueryHandler : IRequestHandler<FileHashExistsQuery, bool>
{
    private readonly IFileRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileHashExistsQueryHandler"/> class.
    /// </summary>
    public FileHashExistsQueryHandler(IFileRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <inheritdoc />
    public async Task<bool> Handle(FileHashExistsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var fileHash = FileHash.From(request.Hash);
        return await _repository.ExistsByHashAsync(fileHash, cancellationToken).ConfigureAwait(false);
    }
}
