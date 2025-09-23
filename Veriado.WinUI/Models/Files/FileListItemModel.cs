using System;

namespace Veriado.Models.Files;

/// <summary>
/// Represents a lightweight projection of <see cref="Contracts.Files.FileSummaryDto"/> tailored for list views.
/// </summary>
public sealed class FileListItemModel
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
    /// Gets or sets the timestamp when the file was created.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; init; }

    /// <summary>
    /// Gets or sets the timestamp when the file was last modified.
    /// </summary>
    public DateTimeOffset LastModifiedUtc { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the file is read-only.
    /// </summary>
    public bool IsReadOnly { get; init; }

    /// <summary>
    /// Gets or sets the optional validity expiration timestamp in UTC.
    /// </summary>
    public DateTimeOffset? ValidUntilUtc { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the search index entry is stale.
    /// </summary>
    public bool IsIndexStale { get; init; }

    /// <summary>
    /// Gets or sets the optional relevance score returned by full-text search.
    /// </summary>
    public double? Score { get; init; }
}
