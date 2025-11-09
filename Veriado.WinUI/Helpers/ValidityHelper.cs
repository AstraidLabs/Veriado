using System;
using System.Globalization;
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

    public static ValidityStatus ComputeStatus(int? daysRemaining)
    {
        if (!daysRemaining.HasValue)
        {
            return ValidityStatus.None;
        }

        return daysRemaining.Value <= 0 ? ValidityStatus.Expired
            : daysRemaining.Value <= 7 ? ValidityStatus.Soon
            : daysRemaining.Value <= 30 ? ValidityStatus.Upcoming
            : ValidityStatus.Ok;
    }

    public static string? BuildTooltip(DateTimeOffset? from, DateTimeOffset? to, int? days)
    {
        if (!from.HasValue || !to.HasValue || !days.HasValue)
        {
            return null;
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            "Platné: {0} – {1} ({2} dní zbývá)",
            from.Value.ToString(DateFormats.ShortDate, CultureInfo.CurrentCulture),
            to.Value.ToString(DateFormats.ShortDate, CultureInfo.CurrentCulture),
            days.Value);
    }
}
