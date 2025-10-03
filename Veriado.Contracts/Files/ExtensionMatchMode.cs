namespace Veriado.Contracts.Files;

/// <summary>
/// Defines the matching strategy for file extension filters.
/// </summary>
public enum ExtensionMatchMode
{
    /// <summary>
    /// Requires an exact case-insensitive match of the extension.
    /// </summary>
    Equals = 0,

    /// <summary>
    /// Applies a case-insensitive substring match to the extension.
    /// </summary>
    Contains = 1,
}
