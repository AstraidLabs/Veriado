using System;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Domain.Search;

namespace Veriado.Application.Abstractions;

/// <summary>
/// Provides an abstraction for interacting with the full-text search indexing infrastructure.
/// </summary>
public interface ISearchIndexer
{
    /// <summary>
    /// Schedules or updates the search index entry for the supplied document.
    /// </summary>
    /// <param name="document">The search document projection.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task UpsertAsync(SearchDocument document, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the search index entry associated with the supplied file identifier.
    /// </summary>
    /// <param name="fileId">The identifier of the file.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task RemoveAsync(Guid fileId, CancellationToken cancellationToken);
}
