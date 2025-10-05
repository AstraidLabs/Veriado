using System;
namespace Veriado.Appl.Search;

/// <summary>
/// Represents the root configuration object for the search subsystem.
/// </summary>
public sealed class SearchOptions
{
    /// <summary>
    /// Gets or sets the scoring configuration applied to Lucene and hybrid queries.
    /// </summary>
    public SearchScoreOptions Score { get; set; } = new();

    /// <summary>
    /// Gets or sets the analyser options shared between indexing and querying.
    /// </summary>
    public AnalyzerOptions Analyzer { get; set; } = new();

    /// <summary>
    /// Gets or sets trigram index related settings.
    /// </summary>
    public TrigramIndexOptions Trigram { get; set; } = new();

    /// <summary>
    /// Gets or sets parser configuration options.
    /// </summary>
    public SearchParseOptions Parse { get; set; } = new();

    /// <summary>
    /// Gets or sets facet aggregation options.
    /// </summary>
    public FacetOptions Facets { get; set; } = new();

    /// <summary>
    /// Gets or sets synonym expansion options.
    /// </summary>
    public SynonymOptions Synonyms { get; set; } = new();

    /// <summary>
    /// Gets or sets suggestion service options.
    /// </summary>
    public SuggesterOptions Suggestions { get; set; } = new();

    /// <summary>
    /// Gets or sets spell suggestion options.
    /// </summary>
    public SpellOptions Spell { get; set; } = new();
}

/// <summary>
/// Provides tuning parameters for BM25 scoring and hybrid result merging.
/// </summary>
public sealed class SearchScoreOptions
{
    public double TitleWeight { get; set; } = 4.0d;
    public double MimeWeight { get; set; } = 0.1d;
    public double AuthorWeight { get; set; } = 2.0d;
    public double MetadataTextWeight { get; set; } = 0.8d;
    public double MetadataWeight { get; set; } = 0.2d;
    public double ScoreMultiplier { get; set; } = 1.0d;
    public bool HigherScoreIsBetter { get; set; }
        = false;
    public bool UseTfIdfAlternative { get; set; }
        = false;
    public double TfIdfDampingFactor { get; set; } = 0.5d;
    public int OversampleMultiplier { get; set; } = 3;
    public double DefaultTrigramScale { get; set; } = 0.45d;
    public double TrigramFloor { get; set; } = 0.30d;
    public string MergeMode { get; set; } = "max";
    public double LuceneWeight { get; set; } = 0.7d;
}

/// <summary>
/// Configures trigram index generation.
/// </summary>
public sealed class TrigramIndexOptions
{
    public int MaxTokens { get; set; } = 2048;

    public string[] Fields { get; set; } =
    {
        "title",
        "author",
        "filename",
        "metadata_text",
    };
}

/// <summary>
/// Specifies defaults for facet aggregations.
/// </summary>
public sealed class FacetOptions
{
    public int TermLimit { get; set; } = 12;
    public int MaxBuckets { get; set; } = 24;

    public string[] SupportedDateIntervals { get; set; } =
    {
        "month",
        "quarter",
        "year",
    };
}

/// <summary>
/// Options controlling synonym expansion.
/// </summary>
public sealed class SynonymOptions
{
    public int MaxVariantsPerTerm { get; set; } = 5;
    public bool EnableCaching { get; set; } = true;
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// Options for the autocomplete suggestion subsystem.
/// </summary>
public sealed class SuggesterOptions
{
    public int MaxSuggestions { get; set; } = 8;
    public int MinTermLength { get; set; } = 3;

    public string[] Sources { get; set; } =
    {
        "title",
        "author",
        "filename",
        "metadata_text",
    };

    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Provides parameters for the trigram based spell-check feature.
/// </summary>
public sealed class SpellOptions
{
    public int MaxSuggestions { get; set; } = 5;
    public double SimilarityThreshold { get; set; } = 0.5d;
    public bool EnableWhenResultsFound { get; set; }
        = false;
}
