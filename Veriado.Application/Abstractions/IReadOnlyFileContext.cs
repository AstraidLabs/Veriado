using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Domain.Files;

namespace Veriado.Appl.Abstractions;

/// <summary>
/// Represents a read-only persistence context exposing queryable file aggregates.
/// </summary>
public interface IReadOnlyFileContext : IAsyncDisposable
{
    /// <summary>
    /// Gets the queryable collection of files.
    /// </summary>
    IQueryable<FileEntity> Files { get; }

    /// <summary>
    /// Materialises the provided query into a list.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="query">The query to execute.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<List<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken);

    /// <summary>
    /// Counts the number of elements in the provided query.
    /// </summary>
    /// <param name="query">The query to count.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<int> CountAsync(IQueryable<FileEntity> query, CancellationToken cancellationToken);
}

/// <summary>
/// Factory for creating <see cref="IReadOnlyFileContext"/> instances.
/// </summary>
public interface IReadOnlyFileContextFactory
{
    /// <summary>
    /// Creates a new read-only context instance.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IReadOnlyFileContext> CreateAsync(CancellationToken cancellationToken);
}
