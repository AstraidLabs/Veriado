using System.Globalization;

namespace Veriado.WinUI.Converters;

public sealed class SizeToHumanConverter : IValueConverter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (!TryGetBytes(value, out var bytes) || bytes < 0)
        {
            return "0 B";
        }

        if (bytes == 0)
        {
            return "0 B";
        }

        var order = Math.Min(Units.Length - 1, (int)Math.Floor(Math.Log(bytes, 1024)));
        var scaled = bytes / Math.Pow(1024, order);
        return string.Create(CultureInfo.GetCultureInfo("cs-CZ"), $"{scaled:0.##} {Units[order]}");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => DependencyProperty.UnsetValue;

    private static bool TryGetBytes(object? value, out double result)
    {
        switch (value)
        {
            case null:
                result = 0;
                return false;
            case double d:
                result = d;
                return true;
            case float f:
                result = f;
                return true;
            case long l:
                result = l;
                return true;
            case int i:
                result = i;
                return true;
            case string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed):
                result = parsed;
                return true;
            default:
                try
                {
                    result = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                    result = 0;
                    return false;
                }
        }
    }
}
