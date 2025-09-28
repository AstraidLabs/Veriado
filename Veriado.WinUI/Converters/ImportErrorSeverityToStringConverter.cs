using Veriado.WinUI.Models.Import;

namespace Veriado.WinUI.Converters;

public sealed class ImportErrorSeverityToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not ImportErrorSeverity severity)
        {
            return string.Empty;
        }

        return severity switch
        {
            ImportErrorSeverity.All => "Vše",
            ImportErrorSeverity.Warning => "Varování",
            ImportErrorSeverity.Error => "Chyby",
            ImportErrorSeverity.Fatal => "Fatální",
            _ => severity.ToString(),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return DependencyProperty.UnsetValue;
    }
}
