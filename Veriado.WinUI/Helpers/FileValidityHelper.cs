using System;
using Microsoft.UI.Xaml.Media;
using Veriado.Contracts.Files;
using Veriado.WinUI.Resources;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Helpers;

/// <summary>
/// Provides helper methods for computing visual metadata about file validity ranges.
/// </summary>
public static class FileValidityHelper
{
    /// <summary>
    /// Attempts to build a validity badge for the supplied validity window.
    /// </summary>
    /// <param name="validity">The validity window to evaluate.</param>
    /// <param name="referenceTime">The reference timestamp used for calculations.</param>
    /// <param name="badge">The resulting badge metadata.</param>
    /// <returns><see langword="true"/> if the badge represents a valid window; otherwise <see langword="false"/>.</returns>
    public static bool TryGetBadge(FileValidityDto? validity, DateTimeOffset referenceTime, out FileValidityBadge badge)
    {
        return TryGetBadge(validity, referenceTime, AppSettings.CreateDefaultValidityThresholds(), out badge);
    }

    /// <summary>
    /// Attempts to build a validity badge for the supplied validity window.
    /// </summary>
    /// <param name="validity">The validity window to evaluate.</param>
    /// <param name="referenceTime">The reference timestamp used for calculations.</param>
    /// <param name="thresholds">The configured badge thresholds.</param>
    /// <param name="badge">The resulting badge metadata.</param>
    /// <returns><see langword="true"/> if the badge represents a valid window; otherwise <see langword="false"/>.</returns>
    public static bool TryGetBadge(
        FileValidityDto? validity,
        DateTimeOffset referenceTime,
        ValidityThresholds thresholds,
        out FileValidityBadge badge)
    {
        if (validity is null
            || validity.ValidUntil < validity.IssuedAt)
        {
            badge = FileValidityBadge.None;
            return false;
        }

        var daysRemaining = ValidityComputation.ComputeDaysRemaining(referenceTime, validity.ValidUntil);
        if (!daysRemaining.HasValue)
        {
            badge = FileValidityBadge.None;
            return false;
        }

        var days = daysRemaining.Value;
        var status = ValidityComputation.ComputeStatus(daysRemaining, thresholds);
        var text = CreateText(status, days);
        var background = SelectBackground(status);
        var foreground = SelectForeground(status);

        badge = new FileValidityBadge(status, days, text, background, foreground);
        return true;
    }

    /// <summary>
    /// Gets a validity badge for the supplied validity window or a default placeholder when unavailable.
    /// </summary>
    /// <param name="validity">The validity window to evaluate.</param>
    /// <param name="referenceTime">The reference timestamp used for calculations.</param>
    /// <returns>The computed badge metadata.</returns>
    public static FileValidityBadge GetBadge(FileValidityDto? validity, DateTimeOffset referenceTime)
    {
        return GetBadge(validity, referenceTime, AppSettings.CreateDefaultValidityThresholds());
    }

    /// <summary>
    /// Gets a validity badge for the supplied validity window or a default placeholder when unavailable.
    /// </summary>
    /// <param name="validity">The validity window to evaluate.</param>
    /// <param name="referenceTime">The reference timestamp used for calculations.</param>
    /// <param name="thresholds">The configured badge thresholds.</param>
    /// <returns>The computed badge metadata.</returns>
    public static FileValidityBadge GetBadge(
        FileValidityDto? validity,
        DateTimeOffset referenceTime,
        ValidityThresholds thresholds)
    {
        TryGetBadge(validity, referenceTime, thresholds, out var badge);
        return badge;
    }

    private static string CreateText(ValidityStatus status, int daysRemaining)
    {
        if (status == ValidityStatus.None)
        {
            return string.Empty;
        }

        if (daysRemaining < 0)
        {
            return "Platnost skončila";
        }

        if (daysRemaining == 0)
        {
            return "Dnes končí";
        }

        return $"Zbývá {CzechPluralization.FormatDays(daysRemaining)}";
    }

    private static Brush SelectBackground(ValidityStatus status)
    {
        return status switch
        {
            ValidityStatus.Expired => AppColorPalette.ValidityExpiredBackgroundBrush,
            ValidityStatus.ExpiringToday => AppColorPalette.ValidityExpiredBackgroundBrush,
            ValidityStatus.ExpiringSoon => AppColorPalette.ValidityExpiringSoonBackgroundBrush,
            ValidityStatus.ExpiringLater => AppColorPalette.ValidityExpiringLaterBackgroundBrush,
            ValidityStatus.LongTerm => AppColorPalette.ValidityLongTermBackgroundBrush,
            _ => AppColorPalette.ValidityLongTermBackgroundBrush,
        };
    }

    private static Brush SelectForeground(ValidityStatus status)
    {
        return status switch
        {
            ValidityStatus.Expired => AppColorPalette.ValidityLightForegroundBrush,
            ValidityStatus.ExpiringToday => AppColorPalette.ValidityLightForegroundBrush,
            ValidityStatus.ExpiringSoon => AppColorPalette.ValidityLightForegroundBrush,
            _ => AppColorPalette.ValidityDarkForegroundBrush,
        };
    }
}

/// <summary>
/// Represents the computed UI metadata for a validity badge.
/// </summary>
/// <param name="Status">The calculated status of the validity window.</param>
/// <param name="DaysRemaining">The number of days remaining relative to the reference time.</param>
/// <param name="Text">The localized label describing the status.</param>
/// <param name="Background">The brush used for the badge background.</param>
/// <param name="Foreground">The brush used for the badge foreground.</param>
public sealed record FileValidityBadge(
    ValidityStatus Status,
    int? DaysRemaining,
    string Text,
    Brush Background,
    Brush Foreground)
{
    /// <summary>
    /// Gets a value indicating whether the badge represents a defined validity range.
    /// </summary>
    public bool HasValidity => Status != ValidityStatus.None;

    /// <summary>
    /// Gets the placeholder badge used when no validity information is available.
    /// </summary>
    public static FileValidityBadge None { get; } = new(
        ValidityStatus.None,
        null,
        string.Empty,
        AppColorPalette.ValidityLongTermBackgroundBrush,
        AppColorPalette.ValidityDarkForegroundBrush);
}
