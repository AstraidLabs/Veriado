namespace Veriado.Infrastructure.Persistence.Audit;

/// <summary>
/// Represents a flattened audit row describing high-level file events.
/// </summary>
internal sealed class FileAuditRecord
{
    public Guid FileId { get; set; }

    public FileAuditAction Action { get; set; }

    public string Description { get; set; } = string.Empty;

    public string? Mime { get; set; }

    public string? Author { get; set; }

    public string? Title { get; set; }

    public UtcTimestamp OccurredUtc { get; set; }
}

/// <summary>
/// Enumerates supported audit actions for file level events.
/// </summary>
internal enum FileAuditAction
{
    Created,
    Renamed,
    MetadataUpdated,
    ReadOnlyChanged,
    ValidityChanged,
}
