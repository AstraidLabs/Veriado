namespace Veriado.Services.Files.Exceptions;

/// <summary>
/// Represents an optimistic concurrency conflict detected while saving file details.
/// </summary>
public sealed class FileDetailConcurrencyException : Exception
{
    public FileDetailConcurrencyException(string message)
        : base(message)
    {
    }
}
