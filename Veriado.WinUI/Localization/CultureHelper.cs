using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Veriado.WinUI.Localization;

/// <summary>
/// Provides helper methods for working with cultures and the WinAppSDK resource system.
/// </summary>
internal static class CultureHelper
{
    private static readonly CultureInfoComparer _cultureComparer = new();
    private static readonly ResourceManager _resourceManager = new();
    private static readonly ResourceMap _resourceMap = _resourceManager.MainResourceMap;

    public static IEqualityComparer<CultureInfo> CultureComparer => _cultureComparer;

    public static IReadOnlyList<CultureInfo> GetSupportedCultures(ResourceMap resourceMap)
    {
        ArgumentNullException.ThrowIfNull(resourceMap);

        return LocalizationConfiguration.SupportedCultures;
    }

    public static CultureInfo DetermineInitialCulture(IReadOnlyList<CultureInfo> supportedCultures)
    {
        if (supportedCultures is null || supportedCultures.Count == 0)
        {
            return CultureInfo.CurrentUICulture;
        }

        var current = CultureInfo.CurrentUICulture;
        var match = supportedCultures.FirstOrDefault(c => _cultureComparer.Equals(c, current));
        return match ?? supportedCultures[0];
    }

    public static void ApplyCulture(CultureInfo culture)
    {
        if (culture is null)
        {
            throw new ArgumentNullException(nameof(culture));
        }

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public static void ApplyCulture(ResourceContext context, CultureInfo culture)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (culture is null)
        {
            throw new ArgumentNullException(nameof(culture));
        }

        context.QualifierValues["Language"] = culture.Name;
    }

    public static string GetString(string resourceKey)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
        {
            throw new ArgumentException("Resource key must be provided.", nameof(resourceKey));
        }

        var context = _resourceManager.CreateResourceContext();
        ApplyCulture(context, CultureInfo.CurrentUICulture);

        try
        {
            var candidate = _resourceMap.GetValue(resourceKey, context);
            if (candidate is not null)
            {
                var value = candidate.ValueAsString;
                return string.IsNullOrEmpty(value) ? resourceKey : value;
            }
        }
        catch
        {
            // Swallow exceptions and fall back to the resource key to avoid crashing the caller.
        }

        return resourceKey;
    }

    private sealed class CultureInfoComparer : IEqualityComparer<CultureInfo>, IComparer<CultureInfo>
    {
        public int Compare(CultureInfo? x, CultureInfo? y)
        {
            return string.Compare(x?.Name, y?.Name, StringComparison.OrdinalIgnoreCase);
        }

        public bool Equals(CultureInfo? x, CultureInfo? y)
        {
            return string.Equals(x?.Name, y?.Name, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(CultureInfo obj)
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name);
        }
    }
}
