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
        return TryGetBadge(validity, referenceTime, ValidityThresholds.Default, out badge);
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

        var referenceDate = referenceTime.ToLocalTime().Date;
        var validUntilDate = validity.ValidUntil.ToLocalTime().Date;
        var daysRemaining = (validUntilDate - referenceDate).Days;
        var status = DetermineStatus(daysRemaining, thresholds);
        var text = CreateText(status, daysRemaining);
        var background = SelectBackground(status, daysRemaining, thresholds);
        var foreground = SelectForeground(status, daysRemaining, thresholds);

        badge = new FileValidityBadge(status, daysRemaining, text, background, foreground);
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
        return GetBadge(validity, referenceTime, ValidityThresholds.Default);
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

    private static FileValidityStatus DetermineStatus(int daysRemaining, ValidityThresholds thresholds)
    {
        if (daysRemaining < 0)
        {
            return FileValidityStatus.Expired;
        }

        if (daysRemaining <= thresholds.RedDays)
        {
            return FileValidityStatus.ExpiringToday;
        }

        if (daysRemaining <= thresholds.OrangeDays)
        {
            return FileValidityStatus.ExpiringSoon;
        }

        if (daysRemaining <= thresholds.GreenDays)
        {
            return FileValidityStatus.ExpiringLater;
        }

        return FileValidityStatus.LongTerm;
    }

    private static string CreateText(FileValidityStatus status, int daysRemaining)
    {
        if (status == FileValidityStatus.None)
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

        return status switch
        {
            _ => $"Zbývá {CzechPluralization.FormatDays(daysRemaining)}",
        };
    }

    private static Brush SelectBackground(FileValidityStatus status, int daysRemaining, ValidityThresholds thresholds)
    {
        return status switch
        {
            FileValidityStatus.Expired => AppColorPalette.ValidityExpiredBackgroundBrush,
            FileValidityStatus.ExpiringToday => AppColorPalette.ValidityExpiredBackgroundBrush,
            FileValidityStatus.ExpiringSoon when daysRemaining <= thresholds.RedDays
                => AppColorPalette.ValidityExpiredBackgroundBrush,
            FileValidityStatus.ExpiringSoon => AppColorPalette.ValidityExpiringSoonBackgroundBrush,
            FileValidityStatus.ExpiringLater => AppColorPalette.ValidityExpiringLaterBackgroundBrush,
            FileValidityStatus.LongTerm => AppColorPalette.ValidityLongTermBackgroundBrush,
            _ => AppColorPalette.ValidityLongTermBackgroundBrush,
        };
    }

    private static Brush SelectForeground(FileValidityStatus status, int daysRemaining, ValidityThresholds thresholds)
    {
        return status switch
        {
            FileValidityStatus.Expired => AppColorPalette.ValidityLightForegroundBrush,
            FileValidityStatus.ExpiringToday => AppColorPalette.ValidityLightForegroundBrush,
            FileValidityStatus.ExpiringSoon when daysRemaining <= thresholds.RedDays
                => AppColorPalette.ValidityLightForegroundBrush,
            FileValidityStatus.ExpiringSoon => AppColorPalette.ValidityLightForegroundBrush,
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
    FileValidityStatus Status,
    int? DaysRemaining,
    string Text,
    Brush Background,
    Brush Foreground)
{
    /// <summary>
    /// Gets a value indicating whether the badge represents a defined validity range.
    /// </summary>
    public bool HasValidity => Status != FileValidityStatus.None;

    /// <summary>
    /// Gets the placeholder badge used when no validity information is available.
    /// </summary>
    public static FileValidityBadge None { get; } = new(
        FileValidityStatus.None,
        null,
        string.Empty,
        AppColorPalette.ValidityLongTermBackgroundBrush,
        AppColorPalette.ValidityDarkForegroundBrush);
}

/// <summary>
/// Enumerates the possible validity statuses used for UI rendering.
/// </summary>
public enum FileValidityStatus
{
    /// <summary>
    /// No validity information is available.
    /// </summary>
    None,

    /// <summary>
    /// The document validity has already expired.
    /// </summary>
    Expired,

    /// <summary>
    /// The document expires within the configured red threshold.
    /// </summary>
    ExpiringToday,

    /// <summary>
    /// The document expires within the configured orange threshold.
    /// </summary>
    ExpiringSoon,

    /// <summary>
    /// The document expires within the configured green threshold.
    /// </summary>
    ExpiringLater,

    /// <summary>
    /// The document validity extends beyond the configured green threshold.
    /// </summary>
    LongTerm,
}
