namespace Veriado.Appl.Common.Exceptions;

/// <summary>
/// Represents a conflict where a newer search projection already exists for the document.
/// </summary>
public sealed class StaleSearchProjectionUpdateException : Exception
{
    public StaleSearchProjectionUpdateException()
        : base("Stale update – newer content already indexed.")
    {
    }

    public StaleSearchProjectionUpdateException(string message)
        : base(message)
    {
    }

    public StaleSearchProjectionUpdateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
