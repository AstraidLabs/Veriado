using Veriado.Contracts.Localization;

namespace Veriado.Contracts.Files;

/// <summary>
/// Provides helper methods for producing localized relative validity descriptions.
/// </summary>
public static class ValidityRelativeFormatter
{
    public static string FormatRemaining(ValidityCountdown countdown)
    {
        if (countdown.YearsRemaining > 0)
        {
            return CzechPluralization.FormatYears(countdown.YearsRemaining);
        }

        if (countdown.MonthsRemaining > 0)
        {
            return CzechPluralization.FormatMonths(countdown.MonthsRemaining);
        }

        if (countdown.WeeksRemaining > 0)
        {
            return CzechPluralization.FormatWeeks(countdown.WeeksRemaining);
        }

        return CzechPluralization.FormatDays(countdown.DaysRemaining);
    }

    public static string FormatAfterExpiration(ValidityCountdown countdown)
    {
        if (countdown.YearsAfterExpiration > 0)
        {
            return CzechPluralization.FormatYears(countdown.YearsAfterExpiration);
        }

        if (countdown.MonthsAfterExpiration > 0)
        {
            return CzechPluralization.FormatMonths(countdown.MonthsAfterExpiration);
        }

        if (countdown.WeeksAfterExpiration > 0)
        {
            return CzechPluralization.FormatWeeks(countdown.WeeksAfterExpiration);
        }

        return CzechPluralization.FormatDays(countdown.DaysAfterExpiration);
    }

    public static string FormatBeforeExpirationPhrase(ValidityCountdown countdown)
    {
        return $"{FormatRemaining(countdown)} do expirace";
    }

    public static string FormatAfterExpirationPhrase(ValidityCountdown countdown)
    {
        return $"{FormatAfterExpiration(countdown)} po expiraci";
    }
}
