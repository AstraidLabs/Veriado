namespace Veriado.Contracts.Files;

/// <summary>
/// Represents the computed time metrics describing the distance to a document's expiration.
/// </summary>
/// <param name="TotalDays">The signed number of whole days between the reference time and expiration.</param>
/// <param name="DaysRemaining">The non-negative number of days until expiration.</param>
/// <param name="DaysAfterExpiration">The non-negative number of days elapsed since expiration.</param>
/// <param name="WeeksRemaining">The non-negative number of weeks until expiration.</param>
/// <param name="WeeksAfterExpiration">The non-negative number of weeks elapsed since expiration.</param>
/// <param name="MonthsRemaining">The non-negative number of months until expiration.</param>
/// <param name="MonthsAfterExpiration">The non-negative number of months elapsed since expiration.</param>
/// <param name="YearsRemaining">The non-negative number of years until expiration.</param>
/// <param name="YearsAfterExpiration">The non-negative number of years elapsed since expiration.</param>
public readonly record struct ValidityCountdown(
    int TotalDays,
    int DaysRemaining,
    int DaysAfterExpiration,
    int WeeksRemaining,
    int WeeksAfterExpiration,
    int MonthsRemaining,
    int MonthsAfterExpiration,
    int YearsRemaining,
    int YearsAfterExpiration)
{
    /// <summary>
    /// Gets a value indicating whether the validity has already expired.
    /// </summary>
    public bool IsExpired => DaysAfterExpiration > 0;

    /// <summary>
    /// Gets a value indicating whether the expiration happens on the same day.
    /// </summary>
    public bool ExpiresToday => TotalDays == 0 && !IsExpired;
}
