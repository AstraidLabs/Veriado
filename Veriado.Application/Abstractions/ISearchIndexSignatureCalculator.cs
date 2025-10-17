namespace Veriado.Appl.Abstractions;

/// <summary>
/// Computes analyzer signatures for indexed file documents.
/// </summary>
public interface ISearchIndexSignatureCalculator
{
    /// <summary>
    /// Computes the signature for the provided aggregate.
    /// </summary>
    /// <param name="file">The file aggregate.</param>
    /// <returns>The computed signature payload.</returns>
    SearchIndexSignature Compute(FileEntity file);

    /// <summary>
    /// Gets the analyzer version hash currently in use.
    /// </summary>
    /// <returns>The analyzer version descriptor.</returns>
    string GetAnalyzerVersion();
}
