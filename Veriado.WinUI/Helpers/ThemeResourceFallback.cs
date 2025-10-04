using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Veriado.WinUI.Helpers;

/// <summary>
/// Provides fallbacks for theme resources that may be missing on older Windows builds.
/// </summary>
internal static class ThemeResourceFallback
{
    private static bool _initialized;

    public static void Ensure()
    {
        if (_initialized)
        {
            return;
        }

        var resources = Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        _initialized = true;

        EnsureAccentAcrylicBrush(resources);
        EnsureAccentStrokeBrush(resources);
        EnsureLayerFillBrush(resources);
        EnsureCardBackgroundBrush(resources);
        EnsureCardStrokeBrush(resources);
        EnsureTextSecondaryBrush(resources);
    }

    private static void EnsureAccentAcrylicBrush(ResourceDictionary resources)
    {
        const string key = "AccentAcrylicBackgroundFillColorDefaultBrush";
        if (resources.ContainsKey(key))
        {
            return;
        }

        var accentColor = GetColorFromBrush(resources, "AppAccentBrush", Color.FromArgb(0xFF, 0x00, 0x63, 0xB1));
        var brush = CreateBrush(accentColor, opacity: 0.24);
        resources[key] = brush;
    }

    private static void EnsureAccentStrokeBrush(ResourceDictionary resources)
    {
        const string key = "AccentStrokeColorDefaultBrush";
        if (resources.ContainsKey(key))
        {
            return;
        }

        var accentColor = GetColorFromBrush(resources, "AppAccentBrush", Color.FromArgb(0xFF, 0x00, 0x63, 0xB1));
        resources[key] = CreateBrush(accentColor);
    }

    private static void EnsureLayerFillBrush(ResourceDictionary resources)
    {
        const string key = "LayerFillColorDefaultBrush";
        if (resources.ContainsKey(key))
        {
            return;
        }

        var surfaceColor = GetColorFromBrush(resources, "AppSurfaceBrush", Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        resources[key] = CreateBrush(surfaceColor, opacity: 0.96);
    }

    private static void EnsureCardBackgroundBrush(ResourceDictionary resources)
    {
        const string key = "CardBackgroundFillColorDefaultBrush";
        if (resources.ContainsKey(key))
        {
            return;
        }

        var surfaceColor = GetColorFromBrush(resources, "AppSurfaceBrush", Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        resources[key] = CreateBrush(surfaceColor, opacity: 0.98);
    }

    private static void EnsureCardStrokeBrush(ResourceDictionary resources)
    {
        const string key = "CardStrokeColorDefaultBrush";
        if (resources.ContainsKey(key))
        {
            return;
        }

        if (resources.TryGetValue("DividerStrokeColorDefaultBrush", out var dividerBrush) && dividerBrush is SolidColorBrush brush)
        {
            resources[key] = CreateBrush(brush.Color, brush.Opacity);
            return;
        }

        resources[key] = CreateBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00));
    }

    private static void EnsureTextSecondaryBrush(ResourceDictionary resources)
    {
        const string key = "TextFillColorSecondaryBrush";
        if (resources.ContainsKey(key))
        {
            return;
        }

        var textColor = GetColorFromBrush(resources, "AppTextPrimaryBrush", Color.FromArgb(0xFF, 0x1B, 0x1F, 0x23));
        resources[key] = CreateBrush(textColor, opacity: 0.72);
    }

    private static SolidColorBrush CreateBrush(Color color, double opacity = 1d)
    {
        var brush = new SolidColorBrush(color)
        {
            Opacity = Math.Clamp(opacity, 0d, 1d),
        };
        return brush;
    }

    private static Color GetColorFromBrush(ResourceDictionary resources, string key, Color fallback)
    {
        if (resources.TryGetValue(key, out var value) && value is SolidColorBrush brush)
        {
            return brush.Color;
        }

        return fallback;
    }
}
