using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Domain.Search;

namespace Veriado.Application.Abstractions;

/// <summary>
/// Provides search capabilities over indexed file documents.
/// </summary>
public interface ISearchQueryService
{
    /// <summary>
    /// Executes a search query and returns the matching documents.
    /// </summary>
    /// <param name="query">The search query text.</param>
    /// <param name="skip">The number of results to skip.</param>
    /// <param name="take">The maximum number of results to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matched search documents.</returns>
    Task<IReadOnlyList<SearchDocument>> QueryAsync(string query, int skip, int take, CancellationToken cancellationToken);
}
