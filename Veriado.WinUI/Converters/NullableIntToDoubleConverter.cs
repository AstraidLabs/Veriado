using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Veriado.WinUI.Converters;

public sealed class NullableIntToDoubleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int intValue)
        {
            return (double)intValue;
        }

        if (value is int?)
        {
            var nullable = (int?)value;
            return nullable.HasValue ? (double)nullable.Value : 0d;
        }

        return 0d;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is double doubleValue)
        {
            if (double.IsNaN(doubleValue) || double.IsInfinity(doubleValue))
            {
                return null;
            }

            var rounded = (int)Math.Round(doubleValue);
            return rounded;
        }

        return DependencyProperty.UnsetValue;
    }
}
