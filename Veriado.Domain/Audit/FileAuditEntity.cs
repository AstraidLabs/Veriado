using System;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.Audit;

/// <summary>
/// Represents an auditable record of aggregate-level file changes.
/// </summary>
public sealed class FileAuditEntity
{
    private FileAuditEntity(Guid fileId, string action, DateTimeOffset occurredAtUtc, string? actor, string? comment)
    {
        FileId = fileId;
        Action = action ?? throw new ArgumentNullException(nameof(action));
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
        Actor = actor;
        Comment = comment;
    }

    /// <summary>
    /// Gets the identifier of the file affected by the audited action.
    /// </summary>
    public Guid FileId { get; }

    /// <summary>
    /// Gets a short action label.
    /// </summary>
    public string Action { get; }

    /// <summary>
    /// Gets optional actor information.
    /// </summary>
    public string? Actor { get; }

    /// <summary>
    /// Gets optional comments describing the change.
    /// </summary>
    public string? Comment { get; }

    /// <summary>
    /// Gets the UTC timestamp when the action occurred.
    /// </summary>
    public DateTimeOffset OccurredAtUtc { get; }

    /// <summary>
    /// Records a file creation audit entry.
    /// </summary>
    public static FileAuditEntity Created(Guid fileId, FileName name, FileExtension extension, string author, DateTimeOffset occurredAtUtc, string? actor = null)
        => new(fileId, "Created", occurredAtUtc, actor, $"Name={name.Value}.{extension.Value};Author={author}");

    /// <summary>
    /// Records a rename audit entry.
    /// </summary>
    public static FileAuditEntity Renamed(Guid fileId, FileName oldName, FileExtension oldExtension, FileName newName, FileExtension newExtension, DateTimeOffset occurredAtUtc, string? actor = null)
        => new(fileId, "Renamed", occurredAtUtc, actor, $"{oldName.Value}.{oldExtension.Value} -> {newName.Value}.{newExtension.Value}");

    /// <summary>
    /// Records metadata changes.
    /// </summary>
    public static FileAuditEntity MetadataChanged(Guid fileId, MimeType oldMime, MimeType newMime, string oldAuthor, string newAuthor, DateTimeOffset occurredAtUtc, string? actor = null)
        => new(fileId, "MetadataChanged", occurredAtUtc, actor, $"Mime: {oldMime.Value} -> {newMime.Value}; Author: {oldAuthor} -> {newAuthor}");

    /// <summary>
    /// Records read-only state changes.
    /// </summary>
    public static FileAuditEntity ReadOnlyToggled(Guid fileId, bool isReadOnly, DateTimeOffset occurredAtUtc, string? actor = null)
        => new(fileId, "ReadOnlyChanged", occurredAtUtc, actor, isReadOnly ? "ReadOnly=1" : "ReadOnly=0");
}
