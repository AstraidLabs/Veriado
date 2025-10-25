namespace Veriado.Infrastructure.Persistence.Entities;

/// <summary>
/// Represents a persisted snapshot of a file content link used for history tracking.
/// </summary>
public sealed class FileContentLinkRow
{
    public Guid FileId { get; set; }

    public int ContentVersion { get; set; }

    public string Provider { get; set; } = null!;

    public string Location { get; set; } = null!;

    public string ContentHash { get; set; } = null!;

    public long SizeBytes { get; set; }

    public string? Mime { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public FileEntity File { get; set; } = null!;
}
