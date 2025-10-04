using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Veriado.WinUI.Localization;

internal static class LocalizedStrings
{
    private static readonly ResourceLoader ResourceLoader = new();

    public static string Get(string resourceKey, string? defaultValue = null, params object?[] arguments)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
        {
            throw new ArgumentException("Resource key must be provided.", nameof(resourceKey));
        }

        var template = ResourceLoader.GetString(resourceKey);

        if (string.IsNullOrWhiteSpace(template))
        {
            template = defaultValue ?? resourceKey;
        }

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
}
