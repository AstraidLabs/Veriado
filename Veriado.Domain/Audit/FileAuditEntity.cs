namespace Veriado.Domain.Audit;

/// <summary>
/// Represents an audit record capturing high-level file events.
/// </summary>
public sealed class FileAuditEntity
{
    private FileAuditEntity(
        Guid fileId,
        FileAuditAction action,
        string description,
        UtcTimestamp occurredUtc,
        string? mime,
        string? author,
        string? title)
    {
        FileId = fileId;
        Action = action;
        Description = description;
        OccurredUtc = occurredUtc;
        Mime = mime;
        Author = author;
        Title = title;
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
    /// Gets the MIME type associated with the audit entry, if any.
    /// </summary>
    public string? Mime { get; }

    /// <summary>
    /// Gets the author associated with the audit entry, if any.
    /// </summary>
    public string? Author { get; }

    /// <summary>
    /// Gets the title associated with the audit entry, if any.
    /// </summary>
    public string? Title { get; }

    /// <summary>
    /// Gets the timestamp when the audit entry was recorded.
    /// </summary>
    public UtcTimestamp OccurredUtc { get; }

    /// <summary>
    /// Creates an audit entry representing file creation.
    /// </summary>
    /// <param name="fileId">The file identifier.</param>
    /// <param name="name">The file name.</param>
    /// <param name="mime">The MIME type recorded at creation.</param>
    /// <param name="author">The author recorded at creation.</param>
    /// <param name="occurredUtc">The timestamp when the event occurred.</param>
    /// <returns>The created audit entry.</returns>
    public static FileAuditEntity Created(Guid fileId, FileName name, MimeType mime, string author, UtcTimestamp occurredUtc)
    {
        return new FileAuditEntity(
            fileId,
            FileAuditAction.Created,
            $"Created as '{name.Value}'",
            occurredUtc,
            mime.Value,
            author,
            title: null);
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
        return new FileAuditEntity(
            fileId,
            FileAuditAction.Renamed,
            $"Renamed from '{oldName.Value}' to '{newName.Value}'",
            occurredUtc,
            mime: null,
            author: null,
            title: null);
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
            occurredUtc,
            mime.Value,
            author,
            string.IsNullOrWhiteSpace(title) ? null : title);
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
        return new FileAuditEntity(
            fileId,
            FileAuditAction.ReadOnlyChanged,
            $"Read-only {state}",
            occurredUtc,
            mime: null,
            author: null,
            title: null);
    }

    /// <summary>
    /// Creates an audit entry representing a document validity change.
    /// </summary>
    /// <param name="fileId">The file identifier.</param>
    /// <param name="issuedAt">The issued timestamp after the change.</param>
    /// <param name="validUntil">The expiration timestamp after the change.</param>
    /// <param name="hasPhysicalCopy">Indicates whether a physical copy exists.</param>
    /// <param name="hasElectronicCopy">Indicates whether an electronic copy exists.</param>
    /// <param name="occurredUtc">The timestamp when the event occurred.</param>
    /// <returns>The created audit entry.</returns>
    public static FileAuditEntity ValidityChanged(
        Guid fileId,
        UtcTimestamp? issuedAt,
        UtcTimestamp? validUntil,
        bool hasPhysicalCopy,
        bool hasElectronicCopy,
        UtcTimestamp occurredUtc)
    {
        var issuedText = issuedAt?.Value.ToString("O") ?? "n/a";
        var validText = validUntil?.Value.ToString("O") ?? "n/a";
        var description =
            $"Validity updated (Issued: {issuedText}, Expires: {validText}, Physical: {hasPhysicalCopy}, Electronic: {hasElectronicCopy})";

        return new FileAuditEntity(
            fileId,
            FileAuditAction.ValidityChanged,
            description,
            occurredUtc,
            mime: null,
            author: null,
            title: null);
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

    /// <summary>
    /// Indicates that the document validity details changed.
    /// </summary>
    ValidityChanged,
}
