namespace Veriado.Domain.Search;

/// <summary>
/// Represents the configuration for the FTS5 tokenizer used by the offline search engine.
/// </summary>
public sealed record Fts5Policy(bool RemoveDiacritics, string Tokenizer, string TokenChars)
{
    /// <summary>
    /// Gets the default policy tuned for Unicode text without tag fields.
    /// </summary>
    public static Fts5Policy Default { get; } = new(true, "unicode61", "-_.");
}
