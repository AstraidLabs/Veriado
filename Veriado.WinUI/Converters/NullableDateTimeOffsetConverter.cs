using System;
using Microsoft.UI.Xaml.Data;

namespace Veriado.WinUI.Converters;

/// <summary>
/// Converts between nullable <see cref="DateTimeOffset"/> values and the non-nullable values required by <see cref="DatePicker"/>.
/// </summary>
public sealed class NullableDateTimeOffsetConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is DateTimeOffset dto)
        {
            return dto;
        }

        var now = DateTimeOffset.Now;
        return new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is DateTimeOffset dto)
        {
            return dto;
        }

        return null;
    }
}
