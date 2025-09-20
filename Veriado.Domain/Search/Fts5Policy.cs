using System;

namespace Veriado.Domain.Search;

/// <summary>
/// Provides hints for configuring FTS5 indexes (tokenizer, diacritic handling, ...).
/// </summary>
public sealed class Fts5Policy
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Fts5Policy"/> class.
    /// </summary>
    /// <param name="tokenizer">Tokenizer hint (e.g. "unicode61").</param>
    /// <param name="tokenizerArguments">Optional arguments for the tokenizer.</param>
    /// <param name="removeDiacritics">Indicates whether diacritics should be removed.</param>
    public Fts5Policy(string? tokenizer = "unicode61", string? tokenizerArguments = null, bool removeDiacritics = true)
    {
        Tokenizer = tokenizer;
        TokenizerArguments = tokenizerArguments;
        RemoveDiacritics = removeDiacritics;
    }

    /// <summary>
    /// Gets the tokenizer hint.
    /// </summary>
    public string? Tokenizer { get; }

    /// <summary>
    /// Gets optional tokenizer arguments.
    /// </summary>
    public string? TokenizerArguments { get; }

    /// <summary>
    /// Gets a value indicating whether diacritics should be removed.
    /// </summary>
    public bool RemoveDiacritics { get; }

    /// <summary>
    /// Gets a default FTS5 policy (unicode61 tokenizer with diacritic stripping).
    /// </summary>
    public static Fts5Policy Default { get; } = new();
}
