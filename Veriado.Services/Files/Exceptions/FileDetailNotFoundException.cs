namespace Veriado.Services.Files.Exceptions;

/// <summary>
/// Represents a missing file when attempting to load or update details.
/// </summary>
public sealed class FileDetailNotFoundException : Exception
{
    public FileDetailNotFoundException(Guid id)
        : base($"File '{id}' was not found.")
    {
        FileId = id;
    }

    public Guid FileId { get; }
}
