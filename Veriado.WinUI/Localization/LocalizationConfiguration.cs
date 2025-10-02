using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace Veriado.WinUI.Localization;

internal static class LocalizationConfiguration
{
    private static readonly CultureInfo[] SupportedCultureDefinitions =
    {
        CultureInfo.GetCultureInfo("en-US"),
        CultureInfo.GetCultureInfo("cs-CZ"),
    };

    public static IReadOnlyList<CultureInfo> SupportedCultures { get; } =
        new ReadOnlyCollection<CultureInfo>(SupportedCultureDefinitions);

    public static CultureInfo DefaultCulture => SupportedCultureDefinitions[0];

    public static CultureInfo NormalizeCulture(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
        {
            return DefaultCulture;
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            return NormalizeCulture(culture);
        }
        catch (CultureNotFoundException)
        {
            return DefaultCulture;
        }
    }

    public static CultureInfo NormalizeCulture(CultureInfo? culture)
    {
        if (culture is null)
        {
            return DefaultCulture;
        }

        var exactMatch = SupportedCultureDefinitions.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, culture.Name, StringComparison.OrdinalIgnoreCase));
        if (exactMatch is not null)
        {
            return exactMatch;
        }

        var twoLetterMatch = SupportedCultureDefinitions.FirstOrDefault(candidate =>
            string.Equals(candidate.TwoLetterISOLanguageName, culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase));
        return twoLetterMatch ?? DefaultCulture;
    }
}
