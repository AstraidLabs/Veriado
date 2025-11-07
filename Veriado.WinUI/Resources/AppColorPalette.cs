namespace Veriado.WinUI.Resources;

using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

public static class AppColorPalette
{
    private const string AppAccentBrushKey = "AppAccentBrush";
    private const string AppBackgroundBrushKey = "AppBackgroundBrush";
    private const string AppSurfaceBrushKey = "AppSurfaceBrush";
    private const string AppNavigationBackgroundBrushKey = "AppNavigationBackgroundBrush";
    private const string AppNavigationForegroundBrushKey = "AppNavigationForegroundBrush";
    private const string AppTextPrimaryBrushKey = "AppTextPrimaryBrush";
    private const string AppValidityExpiredBackgroundBrushKey = "AppValidityExpiredBackgroundBrush";
    private const string AppValidityExpiringSoonBackgroundBrushKey = "AppValidityExpiringSoonBackgroundBrush";
    private const string AppValidityExpiringLaterBackgroundBrushKey = "AppValidityExpiringLaterBackgroundBrush";
    private const string AppValidityLongTermBackgroundBrushKey = "AppValidityLongTermBackgroundBrush";
    private const string AppValidityLightForegroundBrushKey = "AppValidityLightForegroundBrush";
    private const string AppValidityDarkForegroundBrushKey = "AppValidityDarkForegroundBrush";

    public static SolidColorBrush AccentBrush => GetBrush(AppAccentBrushKey);
    public static SolidColorBrush BackgroundBrush => GetBrush(AppBackgroundBrushKey);
    public static SolidColorBrush SurfaceBrush => GetBrush(AppSurfaceBrushKey);
    public static SolidColorBrush NavigationBackgroundBrush => GetBrush(AppNavigationBackgroundBrushKey);
    public static SolidColorBrush NavigationForegroundBrush => GetBrush(AppNavigationForegroundBrushKey);
    public static SolidColorBrush TextPrimaryBrush => GetBrush(AppTextPrimaryBrushKey);

    public static SolidColorBrush ValidityExpiredBackgroundBrush => GetBrush(AppValidityExpiredBackgroundBrushKey);
    public static SolidColorBrush ValidityExpiringSoonBackgroundBrush => GetBrush(AppValidityExpiringSoonBackgroundBrushKey);
    public static SolidColorBrush ValidityExpiringLaterBackgroundBrush => GetBrush(AppValidityExpiringLaterBackgroundBrushKey);
    public static SolidColorBrush ValidityLongTermBackgroundBrush => GetBrush(AppValidityLongTermBackgroundBrushKey);
    public static SolidColorBrush ValidityLightForegroundBrush => GetBrush(AppValidityLightForegroundBrushKey);
    public static SolidColorBrush ValidityDarkForegroundBrush => GetBrush(AppValidityDarkForegroundBrushKey);

    public static Color AccentColor => AccentBrush.Color;
    public static Color BackgroundColor => BackgroundBrush.Color;
    public static Color SurfaceColor => SurfaceBrush.Color;
    public static Color NavigationBackgroundColor => NavigationBackgroundBrush.Color;
    public static Color NavigationForegroundColor => NavigationForegroundBrush.Color;
    public static Color TextPrimaryColor => TextPrimaryBrush.Color;

    public static Color ValidityExpiredBackgroundColor => ValidityExpiredBackgroundBrush.Color;
    public static Color ValidityExpiringSoonBackgroundColor => ValidityExpiringSoonBackgroundBrush.Color;
    public static Color ValidityExpiringLaterBackgroundColor => ValidityExpiringLaterBackgroundBrush.Color;
    public static Color ValidityLongTermBackgroundColor => ValidityLongTermBackgroundBrush.Color;
    public static Color ValidityLightForegroundColor => ValidityLightForegroundBrush.Color;
    public static Color ValidityDarkForegroundColor => ValidityDarkForegroundBrush.Color;

    private static SolidColorBrush GetBrush(string resourceKey)
    {
        return (SolidColorBrush)GetResource(resourceKey);
    }

    private static object GetResource(string resourceKey)
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("Application.Current is not available.");

        if (application.Resources.TryGetValue(resourceKey, out var value) && value is not null)
        {
            return value;
        }

        var mergedMatch = FindInMergedDictionaries(application.Resources.MergedDictionaries, resourceKey);
        if (mergedMatch is not null)
        {
            return mergedMatch;
        }

        throw new InvalidOperationException($"Resource '{resourceKey}' was not found in the application resources.");
    }

    private static object? FindInMergedDictionaries(IList<ResourceDictionary> dictionaries, string resourceKey)
    {
        for (var index = dictionaries.Count - 1; index >= 0; index--)
        {
            var dictionary = dictionaries[index];

            if (dictionary.TryGetValue(resourceKey, out var value) && value is not null)
            {
                return value;
            }

            if (dictionary.MergedDictionaries.Count > 0)
            {
                var nested = FindInMergedDictionaries(dictionary.MergedDictionaries, resourceKey);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }
}
