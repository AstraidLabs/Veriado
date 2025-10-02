using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Globalization;

namespace Veriado.WinUI.Localization;

internal static class CultureHelper
{
    private static readonly ResourceManager ResourceManager = new();

    public static void ApplyCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        ResourceManager.DefaultContext.QualifierValues["Language"] = culture.Name;
        LocalizedStrings.SetLanguageQualifier(culture.Name);
        if (OperatingSystem.IsWindows())
        {
            ApplicationLanguages.PrimaryLanguageOverride = culture.Name;
        }
    }
}
