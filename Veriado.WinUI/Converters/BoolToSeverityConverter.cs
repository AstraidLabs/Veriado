#nullable enable
using System.Globalization;

namespace Veriado.WinUI.Converters;

/// <summary>
/// Converts boolean-like values to <see cref="InfoBarSeverity"/> instances and back.
/// </summary>
public sealed partial class BoolToSeverityConverter : IValueConverter
{
    public InfoBarSeverity TrueValue { get; set; } = InfoBarSeverity.Error;
    public InfoBarSeverity FalseValue { get; set; } = InfoBarSeverity.Informational;
    public InfoBarSeverity NullValue { get; set; } = InfoBarSeverity.Informational;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool? b = TryToBool(value);
        if (b == true) return TrueValue;
        if (b == false) return FalseValue;
        return NullValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        bool? b = null;

        if (value is InfoBarSeverity sev)
        {
            if (sev == TrueValue) b = true;
            else if (sev == FalseValue) b = false;
            else if (sev == NullValue) b = null;
        }
        else
        {
            b = TryToBool(value);
        }

        if (targetType == typeof(bool))
            return b.HasValue ? b.Value : DependencyProperty.UnsetValue;

        if (targetType == typeof(bool?))
            return b;

        if (targetType == typeof(object))
            return b.HasValue ? b.Value : DependencyProperty.UnsetValue;

        return DependencyProperty.UnsetValue;
    }

    private static bool? TryToBool(object value)
    {
        if (value is null) return null;
        if (value is bool b) return b;
        if (value is bool nb) return nb;

        if (value is string s)
        {
            if (bool.TryParse(s, out var parsedBool))
                return parsedBool;

            if (double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands,
                                CultureInfo.InvariantCulture, out var num))
                return Math.Abs(num) > double.Epsilon;

            return null;
        }

        // Numeric primitives – anything non-zero is true
        if (value is sbyte sb) return sb != 0;
        if (value is byte by) return by != 0;
        if (value is short sh) return sh != 0;
        if (value is ushort ush) return ush != 0;
        if (value is int i) return i != 0;
        if (value is uint ui) return ui != 0;
        if (value is long l) return l != 0;
        if (value is ulong ul) return ul != 0;
        if (value is float f) return Math.Abs(f) > float.Epsilon;
        if (value is double d) return Math.Abs(d) > double.Epsilon;
        if (value is decimal dc) return dc != 0m;

        // Specific WinUI helpers
        if (value is Visibility vis) return vis == Visibility.Visible;
        if (value is GridLength gl) return gl.Value > 0;

        return null;
    }
}
