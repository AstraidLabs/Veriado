using System;
using Veriado.WinUI.Helpers;

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
    public ValidityInfo(DateTimeOffset? validFrom, DateTimeOffset? validTo, DateTimeOffset referenceTime)
    {
        HasValidity = validFrom.HasValue && validTo.HasValue;
        DaysRemaining = HasValidity ? ValidityHelper.ComputeDaysRemaining(referenceTime, validTo) : null;
        Status = ValidityHelper.ComputeStatus(DaysRemaining);
        Tooltip = ValidityHelper.BuildTooltip(validFrom, validTo, DaysRemaining);
    }

    public bool HasValidity { get; }

    public int? DaysRemaining { get; }

    public ValidityStatus Status { get; }

    public string? Tooltip { get; }
}
