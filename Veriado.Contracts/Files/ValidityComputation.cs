using System;

namespace Veriado.Contracts.Files;

/// <summary>
/// Provides helper methods for computing document validity metadata.
/// </summary>
public static class ValidityComputation
{
    /// <summary>
    /// Calculates the number of whole days remaining until the document expires.
    /// </summary>
    /// <param name="referenceTime">The reference timestamp used for the calculation.</param>
    /// <param name="validUntil">The optional expiration timestamp.</param>
    /// <returns>The remaining day count or <c>null</c> when the expiration is unknown.</returns>
    public static int? ComputeDaysRemaining(DateTimeOffset referenceTime, DateTimeOffset? validUntil)
    {
        if (!validUntil.HasValue)
        {
            return null;
        }

        var target = validUntil.Value.ToOffset(referenceTime.Offset);
        var remaining = (target.Date - referenceTime.Date).Days;
        return remaining;
    }

    /// <summary>
    /// Evaluates the validity status for the provided remaining days and thresholds.
    /// </summary>
    /// <param name="daysRemaining">The optional remaining day count.</param>
    /// <param name="thresholds">The configured thresholds.</param>
    /// <returns>The computed <see cref="ValidityStatus"/>.</returns>
    public static ValidityStatus ComputeStatus(int? daysRemaining, ValidityThresholds thresholds)
    {
        if (!daysRemaining.HasValue)
        {
            return ValidityStatus.None;
        }

        var normalized = ValidityThresholds.Normalize(thresholds.RedDays, thresholds.OrangeDays, thresholds.GreenDays);
        var days = daysRemaining.Value;

        if (days < 0)
        {
            return ValidityStatus.Expired;
        }

        if (days <= normalized.RedDays)
        {
            return ValidityStatus.ExpiringToday;
        }

        if (days <= normalized.OrangeDays)
        {
            return ValidityStatus.ExpiringSoon;
        }

        if (days <= normalized.GreenDays)
        {
            return ValidityStatus.ExpiringLater;
        }

        return ValidityStatus.LongTerm;
    }

    /// <summary>
    /// Evaluates the validity status directly from the validity window and thresholds.
    /// </summary>
    /// <param name="referenceTime">The reference timestamp used for the calculation.</param>
    /// <param name="validUntil">The optional expiration timestamp.</param>
    /// <param name="thresholds">The configured thresholds.</param>
    /// <returns>The computed <see cref="ValidityStatus"/>.</returns>
    public static ValidityStatus ComputeStatus(
        DateTimeOffset referenceTime,
        DateTimeOffset? validUntil,
        ValidityThresholds thresholds)
    {
        var remaining = ComputeDaysRemaining(referenceTime, validUntil);
        return ComputeStatus(remaining, thresholds);
    }
}
