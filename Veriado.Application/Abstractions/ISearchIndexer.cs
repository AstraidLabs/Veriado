using System;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Domain.Search;

namespace Veriado.Application.Abstractions;

/// <summary>
/// Provides operations for projecting file aggregates into the search index.
/// </summary>
public interface ISearchIndexer
{
    /// <summary>
    /// Indexes or updates the provided search document.
    /// </summary>
    /// <param name="document">The document to index.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task IndexAsync(SearchDocument document, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the search document associated with the file identifier.
    /// </summary>
    /// <param name="fileId">The identifier of the file to remove from the index.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task RemoveAsync(Guid fileId, CancellationToken cancellationToken);
}
