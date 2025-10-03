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

    public static IEqualityComparer<CultureInfo> CultureComparer => _cultureComparer;

    public static IReadOnlyList<CultureInfo> GetSupportedCultures(ResourceMap resourceMap)
    {
        if (resourceMap is null)
        {
            throw new ArgumentNullException(nameof(resourceMap));
        }

        // WinAppSDK does not expose the list of available languages directly. We opt-in to a curated
        // list that reflects the languages shipped with the application resources. If no explicit
        // qualifiers exist we fall back to English and Czech which are the primary languages of the
        // application.
        var knownCultures = new[]
        {
            new CultureInfo("cs-CZ"),
            new CultureInfo("en-US"),
        };

        return Array.AsReadOnly(knownCultures);
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

        var viewIndependentContext = ResourceContext.GetForViewIndependentUse();
        ApplyCulture(viewIndependentContext, culture);
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
