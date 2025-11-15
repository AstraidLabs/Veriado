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
        var countdown = ComputeCountdown(referenceTime, validUntil);
        return countdown?.TotalDays;
    }

    /// <summary>
    /// Calculates the extended countdown metrics describing the distance to the expiration.
    /// </summary>
    /// <param name="referenceTime">The reference timestamp used for the calculation.</param>
    /// <param name="validUntil">The optional expiration timestamp.</param>
    /// <returns>The computed <see cref="ValidityCountdown"/> or <c>null</c> when the expiration is unknown.</returns>
    public static ValidityCountdown? ComputeCountdown(DateTimeOffset referenceTime, DateTimeOffset? validUntil)
    {
        if (!validUntil.HasValue)
        {
            return null;
        }

        var target = validUntil.Value.ToOffset(referenceTime.Offset);
        var totalDays = (target.Date - referenceTime.Date).Days;

        var daysRemaining = Math.Max(totalDays, 0);
        var daysAfterExpiration = Math.Max(-totalDays, 0);

        var referenceDate = referenceTime.Date;
        var targetDate = target.Date;

        var weeksRemaining = daysRemaining / 7;
        var weeksAfterExpiration = daysAfterExpiration / 7;

        var monthsRemaining = totalDays >= 0
            ? CalculateCalendarMonths(referenceDate, targetDate)
            : 0;

        var monthsAfterExpiration = totalDays < 0
            ? CalculateCalendarMonths(targetDate, referenceDate)
            : 0;

        var yearsRemaining = totalDays >= 0
            ? CalculateCalendarYears(referenceDate, targetDate)
            : 0;

        var yearsAfterExpiration = totalDays < 0
            ? CalculateCalendarYears(targetDate, referenceDate)
            : 0;

        return new ValidityCountdown(
            totalDays,
            daysRemaining,
            daysAfterExpiration,
            weeksRemaining,
            weeksAfterExpiration,
            monthsRemaining,
            monthsAfterExpiration,
            yearsRemaining,
            yearsAfterExpiration);
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

        return ComputeStatusInternal(daysRemaining.Value, thresholds);
    }

    /// <summary>
    /// Evaluates the validity status for the provided countdown and thresholds.
    /// </summary>
    /// <param name="countdown">The computed countdown.</param>
    /// <param name="thresholds">The configured thresholds.</param>
    /// <returns>The computed <see cref="ValidityStatus"/>.</returns>
    public static ValidityStatus ComputeStatus(ValidityCountdown? countdown, ValidityThresholds thresholds)
    {
        if (!countdown.HasValue)
        {
            return ValidityStatus.None;
        }

        return ComputeStatusInternal(countdown.Value.TotalDays, thresholds);
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
        var countdown = ComputeCountdown(referenceTime, validUntil);
        return ComputeStatus(countdown, thresholds);
    }

    private static ValidityStatus ComputeStatusInternal(int totalDays, ValidityThresholds thresholds)
    {
        var normalized = ValidityThresholds.Normalize(thresholds.RedDays, thresholds.OrangeDays, thresholds.GreenDays);

        if (totalDays < 0)
        {
            return ValidityStatus.Expired;
        }

        if (totalDays <= normalized.RedDays)
        {
            return ValidityStatus.ExpiringToday;
        }

        if (totalDays <= normalized.OrangeDays)
        {
            return ValidityStatus.ExpiringSoon;
        }

        if (totalDays <= normalized.GreenDays)
        {
            return ValidityStatus.ExpiringLater;
        }

        return ValidityStatus.LongTerm;
    }

    private static int CalculateCalendarMonths(DateTime start, DateTime end)
    {
        if (end <= start)
        {
            return 0;
        }

        var months = (end.Year - start.Year) * 12 + end.Month - start.Month;
        if (end.Day < start.Day)
        {
            months--;
        }

        return Math.Max(months, 0);
    }

    private static int CalculateCalendarYears(DateTime start, DateTime end)
    {
        if (end <= start)
        {
            return 0;
        }

        var years = end.Year - start.Year;
        if (end.Month < start.Month || (end.Month == start.Month && end.Day < start.Day))
        {
            years--;
        }

        return Math.Max(years, 0);
    }
}
