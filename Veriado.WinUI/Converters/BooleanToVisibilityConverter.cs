#nullable enable
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Veriado.WinUI.Converters;

/// <summary>
/// Converts bool / bool? to <see cref="Visibility"/> and back.
/// </summary>
public sealed partial class BooleanToVisibilityConverter : IValueConverter
{
    /// <summary>Visibility returned when the input resolves to true.</summary>
    public Visibility TrueValue { get; set; } = Visibility.Visible;

    /// <summary>Visibility returned when the input resolves to false.</summary>
    public Visibility FalseValue { get; set; } = Visibility.Collapsed;

    /// <summary>Visibility returned when the input cannot be evaluated (null, Unset…).</summary>
    public Visibility NullValue { get; set; } = Visibility.Collapsed;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool? b = ToNullableBool(value);

        if (b == true) return TrueValue;
        if (b == false) return FalseValue;
        return NullValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is not Visibility v)
            return DependencyProperty.UnsetValue;

        bool? b = null;
        if (v == TrueValue) b = true;
        else if (v == FalseValue) b = false;
        else if (v == NullValue) b = null;

        if (targetType == typeof(bool))
            return b.HasValue ? b.Value : DependencyProperty.UnsetValue;

        if (targetType == typeof(bool?))
            return b;

        if (targetType == typeof(object))
            return b.HasValue ? b.Value : DependencyProperty.UnsetValue;

        return DependencyProperty.UnsetValue;
    }

    private static bool? ToNullableBool(object value)
    {
        if (value is null) return null;
        if (value is bool b) return b;
        if (value is bool nb) return nb;
        return null;
    }
}
