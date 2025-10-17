namespace Veriado.Appl.UseCases.Files.RelinkFileContent;

/// <summary>
/// Command used to relink an existing file to freshly uploaded content stored in the external file system.
/// </summary>
/// <param name="FileId">The identifier of the logical file.</param>
/// <param name="Mime">The MIME type reported by the caller.</param>
/// <param name="Content">The new binary content.</param>
public sealed record RelinkFileContentCommand(
    Guid FileId,
    string Mime,
    byte[] Content) : IRequest<AppResult<FileSummaryDto>>;
