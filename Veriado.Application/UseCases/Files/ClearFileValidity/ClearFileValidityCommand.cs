namespace Veriado.Appl.UseCases.Files.ClearFileValidity;

/// <summary>
/// Command to clear validity information from a file.
/// </summary>
/// <param name="FileId">The file identifier.</param>
/// <param name="ExpectedVersion">The optional optimistic concurrency token.</param>
public sealed record ClearFileValidityCommand(Guid FileId, int? ExpectedVersion = null)
    : IRequest<AppResult<FileSummaryDto>>;
