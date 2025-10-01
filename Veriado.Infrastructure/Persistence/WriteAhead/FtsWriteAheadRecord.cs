namespace Veriado.Infrastructure.Persistence.WriteAhead;

internal sealed class FtsWriteAheadRecord
{
    public long Id { get; set; }

    public string FileId { get; set; } = string.Empty;

    public string Operation { get; set; } = string.Empty;

    public string? ContentHash { get; set; }

    public string? TitleHash { get; set; }

    public string EnqueuedUtc { get; set; } = string.Empty;
}
