namespace Veriado.Appl.UseCases.Files.CreateFileWithUpload;

/// <summary>
/// Command used to create a new file aggregate backed by an uploaded blob stored in the external file system.
/// </summary>
/// <param name="Name">The file name without extension.</param>
/// <param name="Extension">The file extension.</param>
/// <param name="Mime">The MIME type reported by the caller.</param>
/// <param name="Author">The document author.</param>
/// <param name="Content">The binary content of the file.</param>
public sealed record CreateFileWithUploadCommand(
    string Name,
    string Extension,
    string Mime,
    string Author,
    byte[] Content) : IRequest<AppResult<Guid>>;
