using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Veriado.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public Visibility TrueValue { get; set; } = Visibility.Visible;

    public Visibility FalseValue { get; set; } = Visibility.Collapsed;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return boolValue ? TrueValue : FalseValue;
        }

        return FalseValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is Visibility visibility)
        {
            if (visibility == TrueValue)
            {
                return true;
            }

            if (visibility == FalseValue)
            {
                return false;
            }
        }

        return false;
    }
}
