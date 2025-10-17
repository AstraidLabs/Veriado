namespace Veriado.Infrastructure.Persistence.Audit;

/// <summary>
/// Represents an audit entry describing logical file linkage to file system resources.
/// </summary>
internal sealed class FileLinkAuditRecord
{
    public Guid FileId { get; set; }

    public Guid FileSystemId { get; set; }

    public FileLinkAuditAction Action { get; set; }

    public int Version { get; set; }

    public string Hash { get; set; } = string.Empty;

    public long Size { get; set; }

    public string Mime { get; set; } = string.Empty;

    public UtcTimestamp OccurredUtc { get; set; }
}

/// <summary>
/// Enumerates supported audit actions for file linkage operations.
/// </summary>
internal enum FileLinkAuditAction
{
    Linked,
    Relinked,
}
