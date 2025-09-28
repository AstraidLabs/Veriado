namespace Veriado.Appl.UseCases.Files.ApplySystemMetadata;

/// <summary>
/// Command to apply a system metadata snapshot to a file.
/// </summary>
/// <param name="FileId">The file identifier.</param>
/// <param name="Attributes">The file attribute flags.</param>
/// <param name="CreatedUtc">The creation timestamp.</param>
/// <param name="LastWriteUtc">The last write timestamp.</param>
/// <param name="LastAccessUtc">The last access timestamp.</param>
/// <param name="OwnerSid">The owner security identifier.</param>
/// <param name="HardLinkCount">The hard link count.</param>
/// <param name="AlternateDataStreamCount">The ADS count.</param>
public sealed record ApplySystemMetadataCommand(
    Guid FileId,
    FileAttributesFlags Attributes,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastWriteUtc,
    DateTimeOffset LastAccessUtc,
    string? OwnerSid,
    uint? HardLinkCount,
    uint? AlternateDataStreamCount) : IRequest<AppResult<FileSummaryDto>>;
