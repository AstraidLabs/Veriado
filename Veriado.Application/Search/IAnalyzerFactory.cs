namespace Veriado.Appl.Search;

/// <summary>
/// Creates configured analyzers for specific language profiles.
/// </summary>
public interface IAnalyzerFactory
{
    /// <summary>
    /// Creates an analyzer for the requested profile or the default profile when <paramref name="profileOrLang"/> is null.
    /// </summary>
    /// <param name="profileOrLang">The optional profile or language identifier.</param>
    /// <returns>The configured analyzer.</returns>
    ITextAnalyzer Create(string? profileOrLang = null);

    /// <summary>
    /// Attempts to locate the configuration profile for the specified identifier.
    /// </summary>
    /// <param name="profileOrLang">The profile or language identifier.</param>
    /// <param name="profile">The resolved profile.</param>
    /// <returns><see langword="true"/> when a profile was found; otherwise <see langword="false"/>.</returns>
    bool TryGetProfile(string profileOrLang, out AnalyzerProfile profile);
}
