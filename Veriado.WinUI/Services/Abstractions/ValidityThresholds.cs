using System;

namespace Veriado.WinUI.Services.Abstractions;

public readonly record struct ValidityThresholds(int RedDays, int OrangeDays, int GreenDays)
{
    public static ValidityThresholds Default { get; } = new(
        AppSettings.DefaultValidityRedThresholdDays,
        AppSettings.DefaultValidityOrangeThresholdDays,
        AppSettings.DefaultValidityGreenThresholdDays);

    public static ValidityThresholds Normalize(int redDays, int orangeDays, int greenDays)
    {
        var normalizedRed = Math.Max(0, redDays);
        var normalizedOrange = Math.Max(normalizedRed, orangeDays);
        var normalizedGreen = Math.Max(normalizedOrange, greenDays);
        return new ValidityThresholds(normalizedRed, normalizedOrange, normalizedGreen);
    }
}
