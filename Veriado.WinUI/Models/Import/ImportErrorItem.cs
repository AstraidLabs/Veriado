using Veriado.Contracts.Import;

namespace Veriado.WinUI.Models.Import;

public sealed class ImportErrorItem
{
    public ImportErrorItem(ImportError source, Guid? fileId = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        Source = source;
        FileId = fileId;
        FileName = string.IsNullOrWhiteSpace(source.FilePath)
            ? "Soubor"
            : System.IO.Path.GetFileName(source.FilePath);
        ErrorMessage = source.Message;
        Code = source.Code;
        Suggestion = source.Suggestion;
        Timestamp = source.Timestamp;
        FilePath = source.FilePath;
        StackTrace = source.StackTrace;
        UniqueKey = BuildKey(source.FilePath, source.Code, source.Message);
        Severity = DetermineSeverity(source.Code);
    }

    public ImportError Source { get; }

    public string FileName { get; }

    public string ErrorMessage { get; }

    public Guid? FileId { get; }

    public string? Code { get; }

    public string? Suggestion { get; }

    public DateTimeOffset Timestamp { get; }

    public string? FilePath { get; }

    public string? StackTrace { get; }

    public string UniqueKey { get; }

    public string FormattedTimestamp => Timestamp.ToLocalTime().ToString("G");

    public ImportErrorSeverity Severity { get; }

    public bool IsWarning => Severity == ImportErrorSeverity.Warning;

    public bool IsFatal => Severity == ImportErrorSeverity.Fatal;

    public bool IsError => Severity == ImportErrorSeverity.Error;

    public bool HasSuggestion => !string.IsNullOrWhiteSpace(Suggestion);

    public bool HasStackTrace => !string.IsNullOrWhiteSpace(StackTrace);

    public bool HasCode => !string.IsNullOrWhiteSpace(Code);

    public bool HasFilePath => !string.IsNullOrWhiteSpace(FilePath);

    public static string BuildKey(string? filePath, string? code, string message)
    {
        return $"{filePath}|{code}|{message}";
    }

    private static ImportErrorSeverity DetermineSeverity(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return ImportErrorSeverity.Error;
        }

        var normalized = code.Trim().ToLowerInvariant();
        if (normalized.Contains("fatal", StringComparison.Ordinal))
        {
            return ImportErrorSeverity.Fatal;
        }

        if (normalized.Contains("warn", StringComparison.Ordinal))
        {
            return ImportErrorSeverity.Warning;
        }

        return ImportErrorSeverity.Error;
    }
}
