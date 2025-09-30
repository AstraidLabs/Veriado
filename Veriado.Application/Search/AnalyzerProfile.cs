using System;

namespace Veriado.Appl.Search;

/// <summary>
/// Represents the configuration applied to a text analyzer profile.
/// </summary>
public sealed class AnalyzerProfile
{
    /// <summary>
    /// Gets or sets the unique profile name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether stemming is enabled when a stemmer is available.
    /// </summary>
    public bool EnableStemming { get; init; }
        = false;

    /// <summary>
    /// Gets or sets a value indicating whether numeric tokens should be preserved.
    /// </summary>
    public bool KeepNumbers { get; init; }
        = true;

    /// <summary>
    /// Gets or sets the stopword list applied after tokenisation.
    /// </summary>
    public string[] Stopwords { get; init; }
        = Array.Empty<string>();

    /// <summary>
    /// Gets or sets a value indicating whether filenames should be split on common delimiters prior to tokenisation.
    /// </summary>
    public bool SplitFilenames { get; init; }
        = true;

    /// <summary>
    /// Gets or sets the optional custom tokenizer identifier.
    /// </summary>
    public string? CustomTokenizer { get; init; }
        = null;

    /// <summary>
    /// Gets or sets additional custom filter identifiers.
    /// </summary>
    public string[] CustomFilters { get; init; }
        = Array.Empty<string>();
}
