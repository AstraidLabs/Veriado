using System.Collections.Generic;

namespace Veriado.Appl.Search;

/// <summary>
/// Represents the configuration store for analyzer profiles.
/// </summary>
public sealed class AnalyzerOptions
{
    /// <summary>
    /// Gets or sets the default profile identifier.
    /// </summary>
    public string DefaultProfile { get; set; } = "cs";

    /// <summary>
    /// Gets or sets the configured analyzer profiles keyed by their identifiers.
    /// </summary>
    public Dictionary<string, AnalyzerProfile> Profiles { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}
