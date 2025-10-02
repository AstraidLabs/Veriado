using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Veriado.WinUI.Localization;

internal static class LocalizedStrings
{
    private static readonly ResourceManager ResourceManager = new();
    private static readonly ResourceMap? ResourceMap = ResourceManager.MainResourceMap.TryGetSubtree("Resources");
    private static readonly ResourceContext ResourceContext = ResourceManager.CreateResourceContext();

    public static string Get(string resourceKey, string? defaultValue = null, params object?[] arguments)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
        {
            throw new ArgumentException("Resource key must be provided.", nameof(resourceKey));
        }

        var template = TryGetString(resourceKey) ?? defaultValue ?? resourceKey;
        if (arguments is { Length: > 0 })
        {
            try
            {
                return string.Format(CultureInfo.CurrentCulture, template, arguments);
            }
            catch (FormatException)
            {
                // Ignore formatting issues and return the template to avoid user-facing crashes.
            }
        }

        return template;
    }

    private static string? TryGetString(string resourceKey)
    {
        if (ResourceMap is null)
        {
            return null;
        }

        var candidate = ResourceMap.TryGetValue(resourceKey, ResourceContext);
        return candidate?.ValueAsString;
    }
}
