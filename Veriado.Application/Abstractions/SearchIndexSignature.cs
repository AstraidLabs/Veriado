namespace Veriado.Appl.Abstractions;

/// <summary>
/// Represents the analyzer signature captured for an indexed document.
/// </summary>
/// <param name="AnalyzerVersion">The analyzer configuration hash.</param>
/// <param name="TokenHash">The hash of generated tokens.</param>
/// <param name="NormalizedTitle">The normalized document title.</param>
public readonly record struct SearchIndexSignature(
    string AnalyzerVersion,
    string? TokenHash,
    string NormalizedTitle);
