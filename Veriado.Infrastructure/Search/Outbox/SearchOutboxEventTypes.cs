using Veriado.Domain.Search.Events;

namespace Veriado.Infrastructure.Search.Outbox;

/// <summary>
/// Provides well-known event type names for search-related outbox payloads.
/// </summary>
internal static class SearchOutboxEventTypes
{
    /// <summary>
    /// Represents an outbox event requesting a Lucene reindex of a file.
    /// </summary>
    public const string ReindexRequested = nameof(SearchReindexRequested);

    /// <summary>
    /// Represents an outbox event requesting removal of a file from the Lucene index.
    /// </summary>
    public const string DeleteRequested = "SearchDeleteRequested";
}
