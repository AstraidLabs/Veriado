using System;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.Audit;

/// <summary>
/// Represents an audit record capturing high-level file events.
/// </summary>
public sealed class FileAuditEntity
{
    private FileAuditEntity(Guid fileId, FileAuditAction action, string description, UtcTimestamp occurredUtc)
    {
        FileId = fileId;
        Action = action;
        Description = description;
        OccurredUtc = occurredUtc;
    }

    /// <summary>
    /// Gets the identifier of the audited file.
    /// </summary>
    public Guid FileId { get; }

    /// <summary>
    /// Gets the audit action.
    /// </summary>
    public FileAuditAction Action { get; }

    /// <summary>
    /// Gets the audit description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the timestamp when the audit entry was recorded.
    /// </summary>
    public UtcTimestamp OccurredUtc { get; }

    /// <summary>
    /// Creates an audit entry representing file creation.
    /// </summary>
    /// <param name="fileId">The file identifier.</param>
    /// <param name="name">The file name.</param>
    /// <param name="occurredUtc">The timestamp when the event occurred.</param>
    /// <returns>The created audit entry.</returns>
    public static FileAuditEntity Created(Guid fileId, FileName name, UtcTimestamp occurredUtc)
    {
        return new FileAuditEntity(fileId, FileAuditAction.Created, $"Created as '{name.Value}'", occurredUtc);
    }

    /// <summary>
    /// Creates an audit entry representing a file rename.
    /// </summary>
    /// <param name="fileId">The file identifier.</param>
    /// <param name="oldName">The previous name.</param>
    /// <param name="newName">The new name.</param>
    /// <param name="occurredUtc">The timestamp when the event occurred.</param>
    /// <returns>The created audit entry.</returns>
    public static FileAuditEntity Renamed(Guid fileId, FileName oldName, FileName newName, UtcTimestamp occurredUtc)
    {
        return new FileAuditEntity(fileId, FileAuditAction.Renamed, $"Renamed from '{oldName.Value}' to '{newName.Value}'", occurredUtc);
    }

    /// <summary>
    /// Creates an audit entry representing a metadata update.
    /// </summary>
    /// <param name="fileId">The file identifier.</param>
    /// <param name="mime">The MIME type after the update.</param>
    /// <param name="author">The author after the update.</param>
    /// <param name="title">The optional title after the update.</param>
    /// <param name="occurredUtc">The timestamp when the event occurred.</param>
    /// <returns>The created audit entry.</returns>
    public static FileAuditEntity MetadataUpdated(Guid fileId, MimeType mime, string author, string? title, UtcTimestamp occurredUtc)
    {
        var titleText = string.IsNullOrWhiteSpace(title) ? "(none)" : $"'{title}'";
        return new FileAuditEntity(
            fileId,
            FileAuditAction.MetadataUpdated,
            $"Metadata updated (MIME: {mime.Value}, Author: {author}, Title: {titleText})",
            occurredUtc);
    }

    /// <summary>
    /// Creates an audit entry representing a read-only flag change.
    /// </summary>
    /// <param name="fileId">The file identifier.</param>
    /// <param name="isReadOnly">The new read-only state.</param>
    /// <param name="occurredUtc">The timestamp when the event occurred.</param>
    /// <returns>The created audit entry.</returns>
    public static FileAuditEntity ReadOnlyChanged(Guid fileId, bool isReadOnly, UtcTimestamp occurredUtc)
    {
        var state = isReadOnly ? "enabled" : "disabled";
        return new FileAuditEntity(fileId, FileAuditAction.ReadOnlyChanged, $"Read-only {state}", occurredUtc);
    }
}

/// <summary>
/// Enumerates supported audit actions for file-level events.
/// </summary>
public enum FileAuditAction
{
    /// <summary>
    /// Indicates that the file was created.
    /// </summary>
    Created,

    /// <summary>
    /// Indicates that the file was renamed.
    /// </summary>
    Renamed,

    /// <summary>
    /// Indicates that metadata was updated.
    /// </summary>
    MetadataUpdated,

    /// <summary>
    /// Indicates that the read-only flag changed.
    /// </summary>
    ReadOnlyChanged,
}
