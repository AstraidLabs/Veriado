namespace Veriado.Infrastructure.Persistence.Audit;

/// <summary>
/// Represents an audit entry capturing file system level events.
/// </summary>
public sealed class FileSystemAuditRecord
{
    public Guid FileSystemId { get; set; }

    public FileSystemAuditAction Action { get; set; }

    public string? Path { get; set; }

    public string? Hash { get; set; }

    public long? Size { get; set; }

    public string? Mime { get; set; }

    public int? Attributes { get; set; }

    public string? OwnerSid { get; set; }

    public bool? IsEncrypted { get; set; }

    public UtcTimestamp OccurredUtc { get; set; }
}

/// <summary>
/// Enumerates supported audit actions for file system events.
/// </summary>
public enum FileSystemAuditAction
{
    ContentChanged,
    Moved,
    AttributesChanged,
    OwnerChanged,
    TimestampsUpdated,
    MissingDetected,
    Rehydrated,
}
