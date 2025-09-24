#nullable enable

using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Veriado.WinUI.Converters;

/// <summary>
/// Converts boolean-like values to <see cref="Visibility"/> values and back.
/// </summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Gets or sets the <see cref="Visibility"/> that is returned when the input resolves to <c>true</c>.
    /// </summary>
    public Visibility TrueValue { get; set; } = Visibility.Visible;

    /// <summary>
    /// Gets or sets the <see cref="Visibility"/> that is returned when the input resolves to <c>false</c>.
    /// </summary>
    public Visibility FalseValue { get; set; } = Visibility.Collapsed;

    /// <summary>
    /// Gets or sets the <see cref="Visibility"/> that is returned when the input cannot be evaluated.
    /// </summary>
    public Visibility NullValue { get; set; } = Visibility.Collapsed;

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
        if (value == DependencyProperty.UnsetValue)
        {
            return DependencyProperty.UnsetValue;
        }

        bool? boolean = value switch
        {
            Visibility visibility when visibility == TrueValue => true,
            Visibility visibility when visibility == FalseValue => false,
            Visibility visibility when visibility == NullValue => (bool?)null,
            _ => TryConvertToBoolean(value)
        };

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
            return boolean.HasValue ? (object)boolean.Value : DependencyProperty.UnsetValue;
        }

        if (targetType == typeof(Visibility))
        {
            return value is Visibility visibility ? visibility : DependencyProperty.UnsetValue;
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
            case Nullable<bool> nullableBool:
                return nullableBool;
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
