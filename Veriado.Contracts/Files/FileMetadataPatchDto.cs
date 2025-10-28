namespace Veriado.Contracts.Files;

/// <summary>
/// Represents a partial update payload for mutable file metadata.
/// </summary>
public sealed class FileMetadataPatchDto
{
    public string? Mime { get; init; }

    public string? Author { get; init; }

    public bool? IsReadOnly { get; init; }
}
