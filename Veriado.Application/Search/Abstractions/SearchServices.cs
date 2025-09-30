using Veriado.Appl.Search;
using Veriado.Domain.Search;

namespace Veriado.Appl.Search.Abstractions;

/// <summary>
/// Provides search capabilities over indexed file documents.
/// </summary>
public interface ISearchQueryService
{
    /// <summary>
    /// Executes an FTS5 search query and returns the matching file identifiers with scores.
    /// </summary>
    /// <param name="plan">The structured query plan.</param>
    /// <param name="skip">The number of results to skip.</param>
    /// <param name="take">The maximum number of results to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching identifiers ordered by relevance.</returns>
    Task<IReadOnlyList<(Guid Id, double Score)>> SearchWithScoresAsync(
        SearchQueryPlan plan,
        int skip,
        int take,
        CancellationToken cancellationToken);

    /// <summary>
    /// Executes a trigram-based fuzzy match query and returns matching identifiers with scores.
    /// </summary>
    /// <param name="plan">The structured query plan.</param>
    /// <param name="skip">The number of results to skip.</param>
    /// <param name="take">The maximum number of results to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching identifiers ordered by relevance.</returns>
    Task<IReadOnlyList<(Guid Id, double Score)>> SearchFuzzyWithScoresAsync(
        SearchQueryPlan plan,
        int skip,
        int take,
        CancellationToken cancellationToken);

    /// <summary>
    /// Counts the number of hits returned by the specified search query.
    /// </summary>
    /// <param name="plan">The structured query plan.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of matching rows.</returns>
    Task<int> CountAsync(SearchQueryPlan plan, CancellationToken cancellationToken);

    /// <summary>
    /// Executes a search query returning hydrated search hits including snippets.
    /// </summary>
    /// <param name="plan">The structured query plan.</param>
    /// <param name="limit">The optional maximum number of results.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matched search hits.</returns>
    Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQueryPlan plan, int? limit, CancellationToken cancellationToken);
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
    /// <param name="isFuzzy">Indicates whether the query was executed using trigram fuzzy search.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task AddAsync(string? queryText, string matchQuery, int totalCount, bool isFuzzy, CancellationToken cancellationToken);

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
    /// <param name="isFuzzy">Indicates whether the favourite represents a trigram query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task AddAsync(string name, string matchQuery, string? queryText, bool isFuzzy, CancellationToken cancellationToken);

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
