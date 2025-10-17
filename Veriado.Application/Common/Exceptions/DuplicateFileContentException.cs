namespace Veriado.Appl.Common.Exceptions;

/// <summary>
/// Represents a conflict caused by attempting to persist duplicate file content hashes.
/// </summary>
public sealed class DuplicateFileContentException : Exception
{
    public DuplicateFileContentException()
    {
    }

    public DuplicateFileContentException(string message)
        : base(message)
    {
    }

    public DuplicateFileContentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
