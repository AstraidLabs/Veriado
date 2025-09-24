#nullable enable

using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

namespace Veriado.Converters;

/// <summary>
/// Converts boolean-like values to <see cref="InfoBarSeverity"/> instances.
/// </summary>
public sealed class BoolToSeverityConverter : IValueConverter
{
    /// <summary>
    /// Gets or sets the severity that is returned when the input resolves to <c>true</c>.
    /// </summary>
    public InfoBarSeverity TrueValue { get; set; } = InfoBarSeverity.Error;

    /// <summary>
    /// Gets or sets the severity that is returned when the input resolves to <c>false</c>.
    /// </summary>
    public InfoBarSeverity FalseValue { get; set; } = InfoBarSeverity.Informational;

    /// <summary>
    /// Gets or sets the severity that is returned when the input cannot be evaluated.
    /// </summary>
    public InfoBarSeverity NullValue { get; set; } = InfoBarSeverity.Informational;

    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var boolean = TryConvertToBoolean(value);

        return boolean switch
        {
            true => TrueValue,
            false => FalseValue,
            null => NullValue
        };
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is InfoBarSeverity severity)
        {
            if (severity == TrueValue)
            {
                return true;
            }

            if (severity == FalseValue)
            {
                return false;
            }

            if (severity == NullValue && targetType == typeof(bool?))
            {
                return null;
            }
        }

        var boolean = TryConvertToBoolean(value);

        if (targetType == typeof(bool))
        {
            return boolean.HasValue ? boolean.Value : DependencyProperty.UnsetValue;
        }

        if (targetType == typeof(bool?))
        {
            return boolean;
        }

        if (targetType == typeof(object))
        {
            return boolean.HasValue ? boolean.Value : DependencyProperty.UnsetValue;
        }

        return DependencyProperty.UnsetValue;
    }

    private static bool? TryConvertToBoolean(object value)
    {
        switch (value)
        {
            case null:
                return null;
            case bool boolValue:
                return boolValue;
            case string text:
                if (bool.TryParse(text, out var parsed))
                {
                    return parsed;
                }

                if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var numeric))
                {
                    return Math.Abs(numeric) > double.Epsilon;
                }

                return null;
            case sbyte sb:
                return sb != 0;
            case byte b:
                return b != 0;
            case short s:
                return s != 0;
            case ushort us:
                return us != 0;
            case int i:
                return i != 0;
            case uint ui:
                return ui != 0;
            case long l:
                return l != 0;
            case ulong ul:
                return ul != 0;
            case float f:
                return Math.Abs(f) > float.Epsilon;
            case double d:
                return Math.Abs(d) > double.Epsilon;
            case decimal dec:
                return dec != 0m;
            case Enum enumValue:
                return System.Convert.ToInt64(enumValue, CultureInfo.InvariantCulture) != 0;
            case Visibility visibility:
                return visibility == Visibility.Visible;
            case GridLength gridLength:
                return gridLength.Value > 0;
        }

        return null;
    }
}
