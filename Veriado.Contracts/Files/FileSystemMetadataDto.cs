namespace Veriado.Contracts.Files;

/// <summary>
/// Represents the file system metadata snapshot captured for a file entity.
/// </summary>
/// <param name="Attributes">The file attribute flags encoded as an integer.</param>
/// <param name="CreatedUtc">The creation timestamp in UTC.</param>
/// <param name="LastWriteUtc">The last write timestamp in UTC.</param>
/// <param name="LastAccessUtc">The last access timestamp in UTC.</param>
/// <param name="OwnerSid">The optional owner security identifier.</param>
/// <param name="HardLinkCount">The optional number of hard links.</param>
/// <param name="AlternateDataStreamCount">The optional number of alternate data streams.</param>
public sealed record FileSystemMetadataDto(
    int Attributes,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastWriteUtc,
    DateTimeOffset LastAccessUtc,
    string? OwnerSid,
    uint? HardLinkCount,
    uint? AlternateDataStreamCount);
