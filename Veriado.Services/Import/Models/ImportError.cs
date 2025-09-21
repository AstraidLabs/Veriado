namespace Veriado.Services.Import.Models;

/// <summary>
/// Represents a failure encountered while importing a file.
/// </summary>
public sealed record ImportError(string FilePath, string Code, string Message);
