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

        if (metrics.TotalDays == 0)
        {
            return "Expirace dnes";
        }

        if (metrics.TotalDays < 0)
        {
            return ValidityRelativeFormatter.FormatAfterExpirationPhrase(metrics);
        }

        return ValidityRelativeFormatter.FormatBeforeExpirationPhrase(metrics);
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

        var metrics = countdown.Value;

        if (metrics.TotalDays < 0)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                "{0} (Platnost skončila před {1})",
                rangeText,
                ValidityRelativeFormatter.FormatAfterExpiration(metrics));
        }

        if (metrics.TotalDays == 0)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                "{0} (Platnost končí dnes: {1})",
                rangeText,
                toText);
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            "{0} ({1})",
            rangeText,
            ValidityRelativeFormatter.FormatBeforeExpirationPhrase(metrics));
    }
}
