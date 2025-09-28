using System;

namespace Veriado.WinUI.Models.Import;

public sealed class ImportErrorItem
{
    public ImportErrorItem(
        string fileName,
        string errorMessage,
        Guid? fileId,
        string? code,
        string? suggestion,
        DateTimeOffset timestamp,
        string? filePath,
        string? stackTrace)
    {
        FileName = fileName;
        ErrorMessage = errorMessage;
        FileId = fileId;
        Code = code;
        Suggestion = suggestion;
        Timestamp = timestamp;
        FilePath = filePath;
        StackTrace = stackTrace;
        UniqueKey = BuildKey(filePath, code, errorMessage);
    }

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

    public bool HasSuggestion => !string.IsNullOrWhiteSpace(Suggestion);

    public bool HasStackTrace => !string.IsNullOrWhiteSpace(StackTrace);

    public bool HasCode => !string.IsNullOrWhiteSpace(Code);

    public bool HasFilePath => !string.IsNullOrWhiteSpace(FilePath);

    public static string BuildKey(string? filePath, string? code, string message)
    {
        return $"{filePath}|{code}|{message}";
    }
}
