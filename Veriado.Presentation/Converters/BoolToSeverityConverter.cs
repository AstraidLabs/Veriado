using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

namespace Veriado.Presentation.Converters;

/// <summary>
/// Converts a boolean flag indicating an error state into an <see cref="InfoBarSeverity"/> value.
/// </summary>
public sealed class BoolToSeverityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool flag)
        {
            return flag ? InfoBarSeverity.Error : InfoBarSeverity.Informational;
        }

        return InfoBarSeverity.Informational;
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
