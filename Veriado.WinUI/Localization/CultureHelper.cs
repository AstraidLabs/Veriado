using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Globalization;
using Veriado.WinUI.Services;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Localization;

internal static class CultureHelper
{
    private static readonly ResourceManager ResourceManager = new();
    private static ResourceContext _resourceContext = new(ResourceManager);

    public static void ApplyCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        _resourceContext = new ResourceContext(ResourceManager);
        _resourceContext.QualifierValues["Language"] = culture.Name;

        if (OperatingSystem.IsWindows())
        {
            ApplicationLanguages.PrimaryLanguageOverride = culture.Name;
        }

        AppServicesLocator.GetLocalizationService()?.RaiseCultureChanged();
    }

    public static string GetString(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Resource key must be provided.", nameof(key));
        }

        return ResourceManager.MainResourceMap
                   .GetValue($"Resources/{key}", _resourceContext)?.ValueAsString
               ?? key;
    }

    private static class AppServicesLocator
    {
        public static LocalizationService? GetLocalizationService()
        {
            try
            {
                if (App.Services.GetService<ILocalizationService>() is LocalizationService localizationService)
                {
                    return localizationService;
                }
            }
            catch
            {
                // Services may not be available during early startup. Ignore failures.
            }

            return null;
        }
    }
}
