namespace Veriado.Appl.Common.Exceptions;

/// <summary>
/// Represents a guard mismatch where the stored analyzer or content hashes drifted from expectations.
/// </summary>
public sealed class AnalyzerOrContentDriftException : Exception
{
    public AnalyzerOrContentDriftException()
    {
    }

    public AnalyzerOrContentDriftException(string message)
        : base(message)
    {
    }

    public AnalyzerOrContentDriftException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
