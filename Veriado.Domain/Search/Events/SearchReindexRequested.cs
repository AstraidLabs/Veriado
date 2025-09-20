using System;
using Veriado.Domain.Primitives;

namespace Veriado.Domain.Search.Events;

/// <summary>
/// Domain event signaling that a file requires search reindexing.
/// </summary>
public sealed class SearchReindexRequested : IDomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SearchReindexRequested"/> class.
    /// </summary>
    /// <param name="fileId">The identifier of the file to reindex.</param>
    /// <param name="reason">The reason for reindexing.</param>
    public SearchReindexRequested(Guid fileId, ReindexReason reason)
    {
        FileId = fileId;
        Reason = reason;
        EventId = Guid.NewGuid();
        OccurredOnUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the file identifier.
    /// </summary>
    public Guid FileId { get; }

    /// <summary>
    /// Gets the reason for reindexing the file.
    /// </summary>
    public ReindexReason Reason { get; }

    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; }
}

/// <summary>
/// Represents the reason for requesting a search index rebuild for a file.
/// </summary>
public enum ReindexReason
{
    /// <summary>
    /// The file has been created.
    /// </summary>
    Created,

    /// <summary>
    /// The file metadata has changed.
    /// </summary>
    MetadataChanged,

    /// <summary>
    /// The file content has changed.
    /// </summary>
    ContentChanged,

    /// <summary>
    /// The validity period has changed.
    /// </summary>
    ValidityChanged,

    /// <summary>
    /// A manual reindex was requested.
    /// </summary>
    Manual,

    /// <summary>
    /// The search schema version increased and requires reindexing.
    /// </summary>
    SchemaUpgrade,
}
