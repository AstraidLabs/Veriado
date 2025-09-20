using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Domain.Search;

namespace Veriado.Application.Search.Abstractions;

/// <summary>
/// Provides search capabilities over indexed file documents.
/// </summary>
public interface ISearchQueryService
{
    /// <summary>
    /// Executes an FTS5 match query and returns the matching file identifiers with scores.
    /// </summary>
    /// <param name="matchQuery">The FTS5 match query.</param>
    /// <param name="skip">The number of results to skip.</param>
    /// <param name="take">The maximum number of results to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching identifiers ordered by relevance.</returns>
    Task<IReadOnlyList<(Guid Id, double Score)>> SearchWithScoresAsync(
        string matchQuery,
        int skip,
        int take,
        CancellationToken cancellationToken);

    /// <summary>
    /// Counts the number of hits returned by the specified match query.
    /// </summary>
    /// <param name="matchQuery">The FTS5 match query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of matching rows.</returns>
    Task<int> CountAsync(string matchQuery, CancellationToken cancellationToken);

    /// <summary>
    /// Executes a search query returning hydrated search hits including snippets.
    /// </summary>
    /// <param name="query">The search query text.</param>
    /// <param name="limit">The optional maximum number of results.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matched search hits.</returns>
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int? limit, CancellationToken cancellationToken);
}

/// <summary>
/// Persists search execution history for recent query recall.
/// </summary>
public interface ISearchHistoryService
{
    /// <summary>
    /// Adds a new history entry or increments the execution count for an existing match.
    /// </summary>
    /// <param name="queryText">The user-supplied query text, if any.</param>
    /// <param name="matchQuery">The generated FTS5 match query.</param>
    /// <param name="totalCount">The number of hits returned by the query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task AddAsync(string? queryText, string matchQuery, int totalCount, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the most recent history entries ordered by recency.
    /// </summary>
    /// <param name="take">The maximum number of entries to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The recent history entries.</returns>
    Task<IReadOnlyList<SearchHistoryEntry>> GetRecentAsync(int take, CancellationToken cancellationToken);

    /// <summary>
    /// Clears history entries optionally retaining the most recent records.
    /// </summary>
    /// <param name="keepLastN">The number of entries to keep, or <see langword="null"/> to delete all.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task ClearAsync(int? keepLastN, CancellationToken cancellationToken);
}

/// <summary>
/// Provides CRUD operations for saved full-text search favourites.
/// </summary>
public interface ISearchFavoritesService
{
    /// <summary>
    /// Retrieves all favourites ordered by their position.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IReadOnlyList<SearchFavoriteItem>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds a new favourite query to the persistent store.
    /// </summary>
    /// <param name="name">The unique display name and key.</param>
    /// <param name="matchQuery">The generated match query.</param>
    /// <param name="queryText">The original query text.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task AddAsync(string name, string matchQuery, string? queryText, CancellationToken cancellationToken);

    /// <summary>
    /// Renames an existing favourite.
    /// </summary>
    /// <param name="id">The favourite identifier.</param>
    /// <param name="newName">The new unique name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task RenameAsync(Guid id, string newName, CancellationToken cancellationToken);

    /// <summary>
    /// Reorders favourites using the provided identifier sequence.
    /// </summary>
    /// <param name="orderedIds">The identifiers ordered by desired position.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task ReorderAsync(IReadOnlyList<Guid> orderedIds, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the specified favourite.
    /// </summary>
    /// <param name="id">The favourite identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task RemoveAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to retrieve a favourite by its key.
    /// </summary>
    /// <param name="key">The unique key (display name).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The favourite when found, otherwise <see langword="null"/>.</returns>
    Task<SearchFavoriteItem?> TryGetByKeyAsync(string key, CancellationToken cancellationToken);
}

/// <summary>
/// Represents a persisted search history entry.
/// </summary>
/// <param name="Id">The entry identifier.</param>
/// <param name="QueryText">The user-supplied query text.</param>
/// <param name="MatchQuery">The generated FTS match clause.</param>
/// <param name="LastQueriedUtc">The timestamp of the most recent execution.</param>
/// <param name="Executions">The number of times the query was executed.</param>
/// <param name="LastTotalHits">The last recorded hit count.</param>
public sealed record SearchHistoryEntry(
    Guid Id,
    string? QueryText,
    string MatchQuery,
    DateTimeOffset LastQueriedUtc,
    int Executions,
    int? LastTotalHits);

/// <summary>
/// Represents a saved search favourite definition.
/// </summary>
/// <param name="Id">The favourite identifier.</param>
/// <param name="Name">The unique favourite name.</param>
/// <param name="QueryText">The original query text.</param>
/// <param name="MatchQuery">The FTS match query.</param>
/// <param name="Position">The ordering position.</param>
/// <param name="CreatedUtc">The creation timestamp.</param>
public sealed record SearchFavoriteItem(
    Guid Id,
    string Name,
    string? QueryText,
    string MatchQuery,
    int Position,
    DateTimeOffset CreatedUtc);
