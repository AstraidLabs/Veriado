using System;
using Veriado.WinUI.Helpers;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.ViewModels.Files;

public enum ValidityStatus
{
    None,
    Ok,
    Upcoming,
    Soon,
    Expired,
}

public sealed class ValidityInfo
{
    public ValidityInfo(
        DateTimeOffset? validFrom,
        DateTimeOffset? validTo,
        DateTimeOffset referenceTime,
        ValidityThresholds thresholds)
    {
        HasValidity = validFrom.HasValue && validTo.HasValue;
        DaysRemaining = HasValidity ? ValidityHelper.ComputeDaysRemaining(referenceTime, validTo) : null;
        DaysRemainingDisplay = DaysRemaining.HasValue
            ? CzechPluralization.FormatDays(DaysRemaining.Value)
            : null;
        Status = ValidityHelper.ComputeStatus(DaysRemaining, thresholds);
        Tooltip = ValidityHelper.BuildTooltip(validFrom, validTo, DaysRemaining);
    }

    public bool HasValidity { get; }

    public int? DaysRemaining { get; }

    public string? DaysRemainingDisplay { get; }

    public ValidityStatus Status { get; }

    public string? Tooltip { get; }
}
