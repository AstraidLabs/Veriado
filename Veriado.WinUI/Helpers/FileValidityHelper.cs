using System;
using Microsoft.Extensions.DependencyInjection;
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
        return TryGetBadge(validity, referenceTime, ResolveThresholds(), out badge);
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

        var countdown = ValidityComputation.ComputeCountdown(referenceTime, validity.ValidUntil);
        if (!countdown.HasValue)
        {
            badge = FileValidityBadge.None;
            return false;
        }

        var status = ValidityComputation.ComputeStatus(countdown, thresholds);
        var text = ValidityDisplayFormatter.BuildBadgeText(status, countdown);
        var glyph = ValidityGlyphProvider.GetGlyph(status);
        var background = SelectBackground(status);
        var foreground = SelectForeground(status);

        badge = new FileValidityBadge(status, countdown, text ?? string.Empty, glyph, background, foreground);
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
        return GetBadge(validity, referenceTime, ResolveThresholds());
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

    private static ValidityThresholds ResolveThresholds()
    {
        try
        {
            if (App.Services.GetService<IHotStateService>() is { } hotState)
            {
                return hotState.ValidityThresholds;
            }
        }
        catch
        {
            // Swallow exceptions during service resolution and fall back to defaults.
        }

        return AppSettings.CreateDefaultValidityThresholds();
    }
}

/// <summary>
/// Represents the computed UI metadata for a validity badge.
/// </summary>
/// <param name="Status">The calculated status of the validity window.</param>
/// <param name="Countdown">The computed countdown metrics for the validity window.</param>
/// <param name="Text">The localized label describing the status.</param>
/// <param name="Glyph">The glyph representing the status.</param>
/// <param name="Background">The brush used for the badge background.</param>
/// <param name="Foreground">The brush used for the badge foreground.</param>
public sealed record FileValidityBadge(
    ValidityStatus Status,
    ValidityCountdown? Countdown,
    string Text,
    string? Glyph,
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
        null,
        AppColorPalette.ValidityLongTermBackgroundBrush,
        AppColorPalette.ValidityDarkForegroundBrush);
}
