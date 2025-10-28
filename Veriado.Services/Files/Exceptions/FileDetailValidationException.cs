namespace Veriado.Services.Files.Exceptions;

/// <summary>
/// Represents a validation failure returned from the application layer when persisting file details.
/// </summary>
public sealed class FileDetailValidationException : Exception
{
    public FileDetailValidationException(string message, IReadOnlyDictionary<string, string[]> errors)
        : base(message)
    {
        Errors = errors ?? throw new ArgumentNullException(nameof(errors));
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
