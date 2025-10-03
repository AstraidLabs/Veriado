using Veriado.Appl.Search;
using Veriado.Contracts.Files;
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
    /// Executes a grid-oriented search query using the centralised SQL pipeline.
    /// </summary>
    /// <param name="matchQuery">The optional FTS5 match query.</param>
    /// <param name="parameters">The grid filter parameters.</param>
    /// <param name="sort">The requested sort specification.</param>
    /// <param name="today">Reference timestamp used for validity filters.</param>
    /// <param name="offset">The zero-based offset.</param>
    /// <param name="limit">The requested page size.</param>
    /// <param name="candidateLimit">The number of candidates evaluated before filtering.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<FileGridSearchResult> SearchGridAsync(
        string? matchQuery,
        FileGridQueryDto parameters,
        IReadOnlyList<FileSortSpecDto> sort,
        DateTimeOffset today,
        int offset,
        int limit,
        int candidateLimit,
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

/// <summary>
/// Describes a single aggregated facet bucket returned by the facet service.
/// </summary>
/// <param name="Value">The bucket key (term, formatted date or numeric range label).</param>
/// <param name="Count">The number of matching documents.</param>
public sealed record FacetValue(string Value, long Count);

/// <summary>
/// Enumerates the supported facet field projections.
/// </summary>
public enum FacetKind
{
    /// <summary>
    /// Simple term facet (GROUP BY over normalised values).
    /// </summary>
    Term,

    /// <summary>
    /// Date histogram bucketed using <c>strftime</c>.
    /// </summary>
    DateHistogram,

    /// <summary>
    /// Numeric range facet (e.g. file size ranges).
    /// </summary>
    NumericRange,
}

/// <summary>
/// Represents an individual facet request.
/// </summary>
/// <param name="Field">The canonical field identifier (mime, author, created, modified, size).</param>
/// <param name="Kind">The facet kind.</param>
/// <param name="Interval">Optional interval specifier for date histograms (day/week/month).</param>
public sealed record FacetField(string Field, FacetKind Kind, string? Interval = null);

/// <summary>
/// Base type for filter constraints applied when computing facets.
/// </summary>
/// <param name="Field">The targeted field identifier.</param>
public abstract record FacetFilter(string Field);

/// <summary>
/// Represents an equality/inclusion filter for term facets.
/// </summary>
/// <param name="Field">The field identifier.</param>
/// <param name="Terms">The allowed terms.</param>
public sealed record TermFacetFilter(string Field, IReadOnlyCollection<string> Terms) : FacetFilter(Field);

/// <summary>
/// Represents a range filter (date or numeric).
/// </summary>
/// <param name="Field">The field identifier.</param>
/// <param name="From">The inclusive lower bound.</param>
/// <param name="To">The inclusive upper bound.</param>
public sealed record RangeFacetFilter(string Field, object? From, object? To) : FacetFilter(Field);

/// <summary>
/// Encapsulates the facet request payload.
/// </summary>
/// <param name="Fields">The facet fields to compute.</param>
/// <param name="Filters">Optional filters applied before aggregation.</param>
public sealed record FacetRequest(
    IReadOnlyCollection<FacetField> Fields,
    IReadOnlyCollection<FacetFilter>? Filters = null);

/// <summary>
/// Provides aggregated document counts for configured facet fields.
/// </summary>
public interface IFacetService
{
    /// <summary>
    /// Computes the requested facet buckets.
    /// </summary>
    /// <param name="request">The facet request definition.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A dictionary mapping field identifiers to ordered facet values.</returns>
    Task<IReadOnlyDictionary<string, IReadOnlyList<FacetValue>>> GetFacetsAsync(
        FacetRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Provides synonym expansion for FTS queries.
/// </summary>
public interface ISynonymProvider
{
    /// <summary>
    /// Expands the supplied token into known synonym variants for the specified language.
    /// </summary>
    /// <param name="language">The ISO language code.</param>
    /// <param name="term">The canonical token.</param>
    /// <returns>A collection containing the canonical term followed by zero or more variants.</returns>
    IReadOnlyList<string> Expand(string language, string term);
}

/// <summary>
/// Represents a spell-correction suggestion.
/// </summary>
/// <param name="Term">The suggested correction.</param>
/// <param name="Score">The similarity score normalised to &lt;0,1&gt;.</param>
public sealed record SpellSuggestion(string Term, double Score);

/// <summary>
/// Provides approximate spell suggestions using trigram similarity.
/// </summary>
public interface ISpellSuggestionService
{
    /// <summary>
    /// Suggests the closest matching terms for the provided token.
    /// </summary>
    /// <param name="token">The misspelt token.</param>
    /// <param name="language">The optional language code.</param>
    /// <param name="limit">The maximum number of suggestions.</param>
    /// <param name="threshold">The minimum similarity threshold.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IReadOnlyList<SpellSuggestion>> SuggestAsync(
        string token,
        string? language,
        int limit,
        double threshold,
        CancellationToken cancellationToken);
}

/// <summary>
/// Represents an autocomplete suggestion entry.
/// </summary>
/// <param name="Term">The suggested term.</param>
/// <param name="Weight">The relative weight used for ordering.</param>
/// <param name="Language">The associated language code.</param>
/// <param name="SourceField">The originating field.</param>
public sealed record SearchSuggestion(string Term, double Weight, string Language, string SourceField);

/// <summary>
/// Provides prefix-based autocomplete suggestions.
/// </summary>
public interface ISearchSuggestionService
{
    /// <summary>
    /// Retrieves suggestions matching the supplied prefix.
    /// </summary>
    /// <param name="prefix">The prefix to match.</param>
    /// <param name="language">Optional language filter.</param>
    /// <param name="limit">The maximum number of results.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IReadOnlyList<SearchSuggestion>> SuggestAsync(
        string prefix,
        string? language,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>
/// Provides telemetry hooks for search operations.
/// </summary>
public interface ISearchTelemetry
{
    /// <summary>
    /// Records the elapsed time for an FTS query.
    /// </summary>
    /// <param name="elapsed">The elapsed duration.</param>
    void RecordFtsQuery(TimeSpan elapsed);

    /// <summary>
    /// Records the elapsed time for a trigram query.
    /// </summary>
    /// <param name="elapsed">The elapsed duration.</param>
    void RecordTrigramQuery(TimeSpan elapsed);

    /// <summary>
    /// Records the elapsed time for a facet computation.
    /// </summary>
    /// <param name="elapsed">The elapsed duration.</param>
    void RecordFacetComputation(TimeSpan elapsed);

    /// <summary>
    /// Records the elapsed time for a composite search (overall latency perceived by callers).
    /// </summary>
    /// <param name="elapsed">The elapsed duration.</param>
    void RecordSearchLatency(TimeSpan elapsed);

    /// <summary>
    /// Updates gauges describing the current index size and document counts.
    /// </summary>
    /// <param name="documentCount">The total number of indexed documents.</param>
    /// <param name="indexSizeBytes">The combined size of FTS and trigram indices, when known.</param>
    void UpdateIndexMetrics(long documentCount, long indexSizeBytes);

    /// <summary>
    /// Records the number of attempts made to process an outbox event.
    /// </summary>
    /// <param name="attempts">The total number of attempts so far.</param>
    void RecordOutboxAttempt(int attempts);

    /// <summary>
    /// Records that an outbox event was moved to the dead-letter queue.
    /// </summary>
    void RecordOutboxDeadLetter();

    /// <summary>
    /// Records the elapsed time of a full-text index verification pass.
    /// </summary>
    /// <param name="elapsed">The elapsed duration.</param>
    void RecordIndexVerificationDuration(TimeSpan elapsed);

    /// <summary>
    /// Records the number of index entries requiring repair after verification.
    /// </summary>
    /// <param name="driftCount">The number of entries detected as missing or drifted.</param>
    void RecordIndexDrift(int driftCount);

    /// <summary>
    /// Records metrics describing paging behaviour and candidate limits of an FTS grid query.
    /// </summary>
    /// <param name="requestedOffset">The requested offset.</param>
    /// <param name="pageSize">The requested page size.</param>
    /// <param name="candidateLimit">The effective candidate limit.</param>
    /// <param name="maxCandidateResults">The configured maximum candidate results.</param>
    /// <param name="returnedCount">The number of items returned on the current page.</param>
    /// <param name="reportedTotalCount">The total count reported to callers (capped).</param>
    /// <param name="actualTotalCount">The actual total count measured.</param>
    /// <param name="hasMore">Indicates whether more pages are available.</param>
    /// <param name="isTruncated">Indicates whether the result set was truncated by policy.</param>
    void RecordFtsPagingMetrics(
        int requestedOffset,
        int pageSize,
        int candidateLimit,
        int maxCandidateResults,
        int returnedCount,
        int reportedTotalCount,
        int actualTotalCount,
        bool hasMore,
        bool isTruncated);
}
