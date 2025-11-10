using System;
using System.Globalization;
using Veriado.Contracts.Files;
using Veriado.WinUI.Helpers;

namespace Veriado.WinUI.ViewModels.Files;

public sealed class ValidityInfo
{
    public ValidityInfo(
        DateTimeOffset? validFrom,
        DateTimeOffset? validTo,
        DateTimeOffset referenceTime,
        ValidityThresholds thresholds)
    {
        HasValidity = validFrom.HasValue && validTo.HasValue && validTo.Value >= validFrom.Value;
        DaysRemaining = HasValidity
            ? ValidityComputation.ComputeDaysRemaining(referenceTime, validTo)
            : null;
        DaysRemainingDisplay = DaysRemaining.HasValue
            ? CzechPluralization.FormatDays(DaysRemaining.Value)
            : null;
        Status = ValidityComputation.ComputeStatus(DaysRemaining, thresholds);
        Tooltip = BuildTooltip(validFrom, validTo, DaysRemaining);
    }

    public bool HasValidity { get; }

    public int? DaysRemaining { get; }

    public string? DaysRemainingDisplay { get; }

    public ValidityStatus Status { get; }

    public string? Tooltip { get; }

    private static string? BuildTooltip(DateTimeOffset? from, DateTimeOffset? to, int? days)
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
