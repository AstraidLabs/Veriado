namespace Veriado.Domain.Search;

/// <summary>
/// Represents the legacy configuration for the tokenizer used by previous FTS-based search implementations.
/// </summary>
public sealed record Fts5Policy(bool RemoveDiacritics, string Tokenizer, string TokenChars)
{
    /// <summary>
    /// Gets the default policy tuned for Unicode text without tag fields.
    /// </summary>
    public static Fts5Policy Default { get; } = new(true, "unicode61", "-_.");
}
