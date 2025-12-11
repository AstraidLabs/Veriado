using Microsoft.Windows.ApplicationModel.Resources;

namespace Veriado.WinUI.Helpers;

internal static class LocalizationHelper
{
    private static readonly ResourceLoader ResourceLoader = new();

    public static string GetString(string key) => ResourceLoader.GetString(key);

    public static string Format(string key, params object[] args) => string.Format(GetString(key), args);
}
