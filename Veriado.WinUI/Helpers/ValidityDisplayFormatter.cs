using System;
using System.Globalization;
using Veriado.Contracts.Files;

namespace Veriado.WinUI.Helpers;

internal static class ValidityDisplayFormatter
{
    public static string? BuildBadgeText(ValidityStatus status, ValidityCountdown? countdown)
    {
        if (status == ValidityStatus.None || !countdown.HasValue)
        {
            return null;
        }

        var metrics = countdown.Value;

        if (status == ValidityStatus.Expired)
        {
            if (metrics.DaysAfterExpiration == 0)
            {
                return "Platnost skončila";
            }

            return $"{ValidityRelativeFormatter.FormatAfterExpiration(metrics)} po expiraci";
        }

        if (metrics.ExpiresToday)
        {
            return "Dnes končí";
        }

        return $"Za {ValidityRelativeFormatter.FormatRemaining(metrics)}";
    }

    public static string? BuildTooltip(
        DateTimeOffset? from,
        DateTimeOffset? to,
        ValidityStatus status,
        ValidityCountdown? countdown)
    {
        if (!from.HasValue || !to.HasValue || !countdown.HasValue)
        {
            return null;
        }

        var fromText = from.Value.ToString(DateFormats.ShortDate, CultureInfo.CurrentCulture);
        var toText = to.Value.ToString(DateFormats.ShortDate, CultureInfo.CurrentCulture);
        var rangeText = string.Format(CultureInfo.CurrentCulture, "Platné: {0} – {1}", fromText, toText);

        return status switch
        {
            ValidityStatus.Expired => string.Format(
                CultureInfo.CurrentCulture,
                "{0} (Platnost skončila před {1})",
                rangeText,
                ValidityRelativeFormatter.FormatAfterExpiration(countdown.Value)),
            ValidityStatus.ExpiringToday => string.Format(
                CultureInfo.CurrentCulture,
                "{0} (Končí dnes)",
                rangeText),
            _ => string.Format(
                CultureInfo.CurrentCulture,
                "{0} (zbývá {1})",
                rangeText,
                ValidityRelativeFormatter.FormatRemaining(countdown.Value)),
        };
    }
}
