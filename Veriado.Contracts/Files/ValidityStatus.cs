namespace Veriado.Contracts.Files;

/// <summary>
/// Enumerates the standardized document validity states shared across application layers.
/// </summary>
public enum ValidityStatus
{
    /// <summary>
    /// No validity window is defined.
    /// </summary>
    None,

    /// <summary>
    /// The document validity has already elapsed.
    /// </summary>
    Expired,

    /// <summary>
    /// The document expires within the critical (red) threshold.
    /// </summary>
    ExpiringToday,

    /// <summary>
    /// The document expires within the warning (orange) threshold.
    /// </summary>
    ExpiringSoon,

    /// <summary>
    /// The document expires within the informational (green) threshold.
    /// </summary>
    ExpiringLater,

    /// <summary>
    /// The document validity extends beyond the configured thresholds.
    /// </summary>
    LongTerm,
}
