using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Veriado.Application.Abstractions;

/// <summary>
/// Executes search queries against the indexed document corpus.
/// </summary>
public interface ISearchQueryService
{
    /// <summary>
    /// Executes a search query and returns ranked hits.
    /// </summary>
    /// <param name="query">The textual query to execute.</param>
    /// <param name="limit">Optional maximum number of hits to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ranked collection of search hits.</returns>
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int? limit, CancellationToken cancellationToken);
}

/// <summary>
/// Represents a single search hit returned by the search infrastructure.
/// </summary>
/// <param name="FileId">The identifier of the matching file.</param>
/// <param name="Title">The indexed title.</param>
/// <param name="Mime">The MIME type of the file.</param>
/// <param name="Snippet">An optional snippet highlighting the match.</param>
/// <param name="Score">The relevance score.</param>
/// <param name="LastModifiedUtc">The last modification timestamp in UTC.</param>
public sealed record SearchHit(
    Guid FileId,
    string Title,
    string Mime,
    string? Snippet,
    double Score,
    DateTimeOffset LastModifiedUtc);
