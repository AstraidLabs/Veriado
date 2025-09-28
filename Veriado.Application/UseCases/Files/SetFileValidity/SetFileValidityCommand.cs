namespace Veriado.Appl.UseCases.Files.SetFileValidity;

/// <summary>
/// Command to set or update document validity information for a file.
/// </summary>
/// <param name="FileId">The file identifier.</param>
/// <param name="IssuedAtUtc">The timestamp when the document was issued.</param>
/// <param name="ValidUntilUtc">The timestamp when the document expires.</param>
/// <param name="HasPhysicalCopy">Indicates whether a physical copy exists.</param>
/// <param name="HasElectronicCopy">Indicates whether an electronic copy exists.</param>
public sealed record SetFileValidityCommand(
    Guid FileId,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ValidUntilUtc,
    bool HasPhysicalCopy,
    bool HasElectronicCopy) : IRequest<AppResult<FileSummaryDto>>;
