using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace Veriado.WinUI.Localization;

internal static class LocalizationConfiguration
{
    private static readonly CultureInfo[] SupportedCultureDefinitions =
    {
        CultureInfo.GetCultureInfo("en-US"),
        CultureInfo.GetCultureInfo("bg-BG"),
        CultureInfo.GetCultureInfo("cs-CZ"),
        CultureInfo.GetCultureInfo("da-DK"),
        CultureInfo.GetCultureInfo("de-DE"),
        CultureInfo.GetCultureInfo("el-GR"),
        CultureInfo.GetCultureInfo("en-IE"),
        CultureInfo.GetCultureInfo("es-ES"),
        CultureInfo.GetCultureInfo("et-EE"),
        CultureInfo.GetCultureInfo("fi-FI"),
        CultureInfo.GetCultureInfo("fr-FR"),
        CultureInfo.GetCultureInfo("ga-IE"),
        CultureInfo.GetCultureInfo("hr-HR"),
        CultureInfo.GetCultureInfo("hu-HU"),
        CultureInfo.GetCultureInfo("it-IT"),
        CultureInfo.GetCultureInfo("lt-LT"),
        CultureInfo.GetCultureInfo("lv-LV"),
        CultureInfo.GetCultureInfo("mt-MT"),
        CultureInfo.GetCultureInfo("nl-NL"),
        CultureInfo.GetCultureInfo("pl-PL"),
        CultureInfo.GetCultureInfo("pt-PT"),
        CultureInfo.GetCultureInfo("ro-RO"),
        CultureInfo.GetCultureInfo("sk-SK"),
        CultureInfo.GetCultureInfo("sl-SI"),
        CultureInfo.GetCultureInfo("sv-SE"),
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
