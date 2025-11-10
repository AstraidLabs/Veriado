using System;
using System.Globalization;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.ViewModels.Files;

namespace Veriado.WinUI.Helpers;

public static class ValidityHelper
{
    public static int? ComputeDaysRemaining(DateTimeOffset now, DateTimeOffset? validTo)
    {
        if (!validTo.HasValue)
        {
            return null;
        }

        return (validTo.Value.Date - now.Date).Days;
    }

    public static ValidityStatus ComputeStatus(int? daysRemaining, ValidityThresholds thresholds)
    {
        if (!daysRemaining.HasValue)
        {
            return ValidityStatus.None;
        }

        var days = daysRemaining.Value;

        if (days <= 0)
        {
            return ValidityStatus.Expired;
        }

        if (days <= thresholds.OrangeDays)
        {
            return ValidityStatus.Soon;
        }

        if (days <= thresholds.GreenDays)
        {
            return ValidityStatus.Upcoming;
        }

        return ValidityStatus.Ok;
    }

    public static string? BuildTooltip(DateTimeOffset? from, DateTimeOffset? to, int? days)
    {
        if (!from.HasValue || !to.HasValue || !days.HasValue)
        {
            return null;
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            "Platné: {0} – {1} (zbývá {2})",
            from.Value.ToString(DateFormats.ShortDate, CultureInfo.CurrentCulture),
            to.Value.ToString(DateFormats.ShortDate, CultureInfo.CurrentCulture),
            CzechPluralization.FormatDays(days.Value));
    }
}
