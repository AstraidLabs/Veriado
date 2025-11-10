using System;

namespace Veriado.Contracts.Files;

/// <summary>
/// Represents the configurable thresholds that partition validity windows into warning stages.
/// </summary>
/// <param name="RedDays">The inclusive number of days considered critical.</param>
/// <param name="OrangeDays">The inclusive number of days considered soon expiring.</param>
/// <param name="GreenDays">The inclusive number of days considered upcoming.</param>
public readonly record struct ValidityThresholds(int RedDays, int OrangeDays, int GreenDays)
{
    /// <summary>
    /// Normalizes the supplied thresholds to ensure they form an ordered, non-negative range.
    /// </summary>
    public static ValidityThresholds Normalize(int redDays, int orangeDays, int greenDays)
    {
        var normalizedRed = Math.Max(0, redDays);
        var normalizedOrange = Math.Max(normalizedRed, orangeDays);
        var normalizedGreen = Math.Max(normalizedOrange, greenDays);
        return new ValidityThresholds(normalizedRed, normalizedOrange, normalizedGreen);
    }
}
