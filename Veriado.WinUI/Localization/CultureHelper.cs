using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Globalization;

namespace Veriado.WinUI.Localization;

internal static class CultureHelper
{
    public static void ApplyCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        ResourceContext.SetGlobalQualifierValue("Language", culture.Name);
        if (OperatingSystem.IsWindows())
        {
            ApplicationLanguages.PrimaryLanguageOverride = culture.Name;
        }
    }
}
