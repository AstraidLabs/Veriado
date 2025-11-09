namespace Veriado.Appl.UseCases.Files.DeleteFile;

/// <summary>
/// Command for deleting a file aggregate.
/// </summary>
/// <param name="FileId">The identifier of the file to delete.</param>
public sealed record DeleteFileCommand(Guid FileId) : IRequest<AppResult<Guid>>;
