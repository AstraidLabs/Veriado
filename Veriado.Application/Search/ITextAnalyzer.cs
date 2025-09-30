namespace Veriado.Appl.Search;

/// <summary>
/// Provides normalization and tokenization services for search text.
/// </summary>
public interface ITextAnalyzer
{
    /// <summary>
    /// Normalises the supplied text using the configured pipeline.
    /// </summary>
    /// <param name="text">The input text.</param>
    /// <param name="profileOrLang">The optional profile or language identifier.</param>
    /// <returns>The normalised text.</returns>
    string Normalize(string text, string? profileOrLang = null);

    /// <summary>
    /// Tokenises the supplied text using the configured pipeline.
    /// </summary>
    /// <param name="text">The input text.</param>
    /// <param name="profileOrLang">The optional profile or language identifier.</param>
    /// <returns>The produced tokens.</returns>
    IEnumerable<string> Tokenize(string text, string? profileOrLang = null);
}
