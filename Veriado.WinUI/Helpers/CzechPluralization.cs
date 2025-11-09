using System;

namespace Veriado.WinUI.Helpers;

internal static class CzechPluralization
{
    public static string FormatDays(int days)
    {
        return $"{days} {GetDayNoun(days)}";
    }

    public static string GetDayNoun(int days)
    {
        var absoluteValue = Math.Abs(days);
        var remainder100 = absoluteValue % 100;

        if (remainder100 is >= 11 and <= 14)
        {
            return "dní";
        }

        return (absoluteValue % 10) switch
        {
            1 => "den",
            2 or 3 or 4 => "dny",
            _ => "dní",
        };
    }
}
