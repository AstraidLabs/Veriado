namespace Veriado.Domain.Search;

/// <summary>
/// Represents a synonym variant tied to a canonical term for a specific language.
/// </summary>
public sealed class SynonymEntry
{
    /// <summary>
    /// Gets the surrogate identifier for the synonym entry.
    /// </summary>
    public int Id { get; private set; }

    /// <summary>
    /// Gets the ISO language code the synonym applies to.
    /// </summary>
    public string Language { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the canonical term that owns the synonym variants.
    /// </summary>
    public string Term { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the synonym variant.
    /// </summary>
    public string Variant { get; private set; } = string.Empty;
}
