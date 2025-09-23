using System;
using System.Collections.Generic;
using Veriado.Contracts.Files;

namespace Veriado.Models.Files;

/// <summary>
/// Represents a presentation-friendly projection of <see cref="FileDetailDto"/>.
/// </summary>
public sealed class FileDetailModel
{
    /// <summary>
    /// Gets or sets the file identifier.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets or sets the file name without extension.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the file extension without the leading dot.
    /// </summary>
    public string Extension { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the MIME type of the file.
    /// </summary>
    public string Mime { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the author assigned to the file.
    /// </summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the file size in bytes.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// Gets or sets the creation timestamp in UTC.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; init; }

    /// <summary>
    /// Gets or sets the last modification timestamp in UTC.
    /// </summary>
    public DateTimeOffset LastModifiedUtc { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the file is read-only.
    /// </summary>
    public bool IsReadOnly { get; init; }

    /// <summary>
    /// Gets or sets the aggregate content version.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Gets or sets the binary content metadata.
    /// </summary>
    public FileContentDto Content { get; init; } = new(string.Empty, 0L);

    /// <summary>
    /// Gets or sets the captured file system metadata.
    /// </summary>
    public FileSystemMetadataDto SystemMetadata { get; init; }
        = new(0, DateTimeOffset.MinValue, DateTimeOffset.MinValue, DateTimeOffset.MinValue, null, null, null);

    /// <summary>
    /// Gets or sets the structured extended metadata entries mapped to display-friendly values.
    /// </summary>
    public IReadOnlyDictionary<string, string?> ExtendedMetadata { get; init; }
        = new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the optional validity information.
    /// </summary>
    public FileValidityDto? Validity { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the search index entry is stale.
    /// </summary>
    public bool IsIndexStale { get; init; }

    /// <summary>
    /// Gets or sets the timestamp of the last successful indexing operation.
    /// </summary>
    public DateTimeOffset? LastIndexedUtc { get; init; }

    /// <summary>
    /// Gets or sets the indexed title stored in the search index.
    /// </summary>
    public string? IndexedTitle { get; init; }

    /// <summary>
    /// Gets or sets the schema version of the search index entry.
    /// </summary>
    public int IndexSchemaVersion { get; init; }

    /// <summary>
    /// Gets or sets the indexed content hash when available.
    /// </summary>
    public string? IndexedContentHash { get; init; }

    /// <summary>
    /// Gets the optional validity expiration timestamp in UTC.
    /// </summary>
    public DateTimeOffset? ValidUntilUtc => Validity?.ValidUntil;
}
