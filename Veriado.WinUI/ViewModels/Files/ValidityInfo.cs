using System;
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
        Countdown = HasValidity
            ? ValidityComputation.ComputeCountdown(referenceTime, validTo)
            : null;
        Status = ValidityComputation.ComputeStatus(Countdown, thresholds);
        DaysRemaining = Countdown?.DaysRemaining;
        DaysAfterExpiration = Countdown?.DaysAfterExpiration;
        DaysRemainingDisplay = ValidityDisplayFormatter.BuildBadgeText(Status, Countdown);
        Tooltip = ValidityDisplayFormatter.BuildTooltip(validFrom, validTo, Status, Countdown);
        BadgeGlyph = ValidityGlyphProvider.GetGlyph(Status);
    }

    public bool HasValidity { get; }

    public ValidityCountdown? Countdown { get; }

    public int? DaysRemaining { get; }

    public int? DaysAfterExpiration { get; }

    public string? DaysRemainingDisplay { get; }

    public ValidityStatus Status { get; }

    public string? Tooltip { get; }

    public string? BadgeGlyph { get; }
}
