using System;

namespace Veriado.Contracts.Localization;

/// <summary>
/// Provides Czech pluralization helpers for time units.
/// </summary>
public static class CzechPluralization
{
    public static string FormatDays(int days)
    {
        var absolute = Math.Abs(days);
        return $"{absolute} {DayWord(absolute)}";
    }

    public static string DayWord(int days) => SelectNoun(Math.Abs(days), "den", "dny", "dní");

    public static string GetDayNoun(int days)
    {
        return DayWord(Math.Abs(days));
    }

    public static string FormatWeeks(int weeks)
    {
        var absolute = Math.Abs(weeks);
        return $"{absolute} {WeekWord(absolute)}";
    }

    public static string WeekWord(int weeks) => SelectNoun(Math.Abs(weeks), "týden", "týdny", "týdnů");

    public static string GetWeekNoun(int weeks)
    {
        return WeekWord(Math.Abs(weeks));
    }

    public static string FormatMonths(int months)
    {
        var absolute = Math.Abs(months);
        return $"{absolute} {MonthWord(absolute)}";
    }

    public static string MonthWord(int months) => SelectNoun(Math.Abs(months), "měsíc", "měsíce", "měsíců");

    public static string GetMonthNoun(int months)
    {
        return MonthWord(Math.Abs(months));
    }

    public static string FormatYears(int years)
    {
        var absolute = Math.Abs(years);
        return $"{absolute} {YearWord(absolute)}";
    }

    public static string YearWord(int years) => SelectNoun(Math.Abs(years), "rok", "roky", "let");

    public static string GetYearNoun(int years)
    {
        return YearWord(Math.Abs(years));
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
