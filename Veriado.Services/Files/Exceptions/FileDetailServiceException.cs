namespace Veriado.Services.Files.Exceptions;

/// <summary>
/// Represents a generic failure when persisting file details.
/// </summary>
public sealed class FileDetailServiceException : Exception
{
    public FileDetailServiceException(string message)
        : base(message)
    {
    }
}
