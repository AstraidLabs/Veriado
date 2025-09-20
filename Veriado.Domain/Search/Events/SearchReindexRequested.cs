using System;
using Veriado.Domain.Primitives;

namespace Veriado.Domain.Search.Events;

/// <summary>
/// Domain event requesting that a file be reindexed for full-text search purposes.
/// </summary>
public sealed record SearchReindexRequested(Guid FileId, SearchReindexReason Reason, DateTimeOffset RequestedAtUtc) : IDomainEvent;

/// <summary>
/// Enumerates reasons that can trigger a search reindex operation.
/// </summary>
public enum SearchReindexReason
{
    /// <summary>
    /// File was created and has never been indexed before.
    /// </summary>
    Created,

    /// <summary>
    /// Non-content metadata relevant for search was changed.
    /// </summary>
    MetadataChanged,

    /// <summary>
    /// File content changed and needs reindexing.
    /// </summary>
    ContentChanged,

    /// <summary>
    /// Validity information changed and should be reindexed.
    /// </summary>
    ValidityChanged,

    /// <summary>
    /// Manual reindex was requested (e.g. administrative action).
    /// </summary>
    Manual,

    /// <summary>
    /// Search schema version increased requiring reindex.
    /// </summary>
    SchemaUpgrade
}
