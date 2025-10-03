namespace Veriado.WinUI.Errors;

public sealed class InitializationException : Exception
{
    public string? Hint { get; }

    public InitializationException(string message, Exception? inner = null, string? hint = null)
        : base(message, inner)
    {
        Hint = hint;
    }
}
