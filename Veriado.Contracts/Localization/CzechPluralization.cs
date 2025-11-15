using System;

namespace Veriado.Contracts.Localization;

/// <summary>
/// Provides Czech pluralization helpers for time units.
/// </summary>
public static class CzechPluralization
{
    public static string FormatDays(int days) => $"{days} {GetDayNoun(days)}";

    public static string GetDayNoun(int days)
    {
        return SelectNoun(days, "den", "dny", "dní");
    }

    public static string FormatWeeks(int weeks) => $"{weeks} {GetWeekNoun(weeks)}";

    public static string GetWeekNoun(int weeks)
    {
        return SelectNoun(weeks, "týden", "týdny", "týdnů");
    }

    public static string FormatMonths(int months) => $"{months} {GetMonthNoun(months)}";

    public static string GetMonthNoun(int months)
    {
        return SelectNoun(months, "měsíc", "měsíce", "měsíců");
    }

    public static string FormatYears(int years) => $"{years} {GetYearNoun(years)}";

    public static string GetYearNoun(int years)
    {
        return SelectNoun(years, "rok", "roky", "let");
    }

    private static string SelectNoun(int value, string singular, string paucal, string plural)
    {
        var absoluteValue = Math.Abs(value);
        var remainder100 = absoluteValue % 100;

        if (absoluteValue == 1)
        {
            return singular;
        }

        if (remainder100 is >= 11 and <= 14)
        {
            return plural;
        }

        return (absoluteValue % 10) switch
        {
            2 or 3 or 4 => paucal,
            _ => plural,
        };
    }
}
