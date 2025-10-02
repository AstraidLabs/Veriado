using System.Globalization;

namespace Veriado.WinUI.Localization;

internal static class LocalizedStrings
{
    public static string Get(string resourceKey, string? defaultValue = null, params object?[] arguments)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
        {
            throw new ArgumentException("Resource key must be provided.", nameof(resourceKey));
        }

        var template = CultureHelper.GetString(resourceKey);
        if (string.Equals(template, resourceKey, StringComparison.Ordinal))
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
