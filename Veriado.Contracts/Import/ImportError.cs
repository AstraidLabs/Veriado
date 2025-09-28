namespace Veriado.Contracts.Import;

/// <summary>
/// Represents a failure encountered while importing a file.
/// </summary>
/// <param name="FilePath">The path of the file that failed to import.</param>
/// <param name="Code">The machine readable error code.</param>
/// <param name="Message">The human readable error description.</param>
/// <param name="Suggestion">Optional guidance for resolving the error.</param>
/// <param name="StackTrace">Optional stack trace captured for diagnostics.</param>
/// <param name="Timestamp">The UTC timestamp when the error was recorded.</param>
public sealed record ImportError(
    string FilePath,
    string Code,
    string Message,
    string? Suggestion,
    string? StackTrace,
    DateTimeOffset Timestamp);
