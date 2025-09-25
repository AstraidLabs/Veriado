using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Veriado.WinUI.Converters;

public sealed class SizeToHumanReadableConverter : IValueConverter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is null)
        {
            return string.Empty;
        }

        double bytes;
        try
        {
            bytes = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return string.Empty;
        }

        if (bytes <= 0)
        {
            return "0 B";
        }

        var order = (int)Math.Min(Units.Length - 1, Math.Log(bytes, 1024));
        var scaled = bytes / Math.Pow(1024, order);
        return string.Create(CultureInfo.GetCultureInfo("cs-CZ"), $"{scaled:0.##} {Units[order]}");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => DependencyProperty.UnsetValue;
}

public sealed class DateTimeToRelativeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is null)
        {
            return string.Empty;
        }

        DateTimeOffset timestamp = value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(dt),
            _ => default
        };

        if (timestamp == default)
        {
            return string.Empty;
        }

        var now = DateTimeOffset.UtcNow;
        var delta = now - timestamp.ToUniversalTime();

        if (delta.TotalSeconds < 60)
        {
            var seconds = Math.Max(1, (int)Math.Round(delta.TotalSeconds));
            return seconds == 1 ? "před sekundou" : $"před {seconds} sekundami";
        }

        if (delta.TotalMinutes < 60)
        {
            var minutes = Math.Max(1, (int)Math.Round(delta.TotalMinutes));
            return minutes == 1 ? "před minutou" : $"před {minutes} minutami";
        }

        if (delta.TotalHours < 24)
        {
            var hours = Math.Max(1, (int)Math.Round(delta.TotalHours));
            return hours == 1 ? "před hodinou" : $"před {hours} hodinami";
        }

        if (delta.TotalDays < 7)
        {
            var days = Math.Max(1, (int)Math.Round(delta.TotalDays));
            return days == 1 ? "včera" : $"před {days} dny";
        }

        if (delta.TotalDays < 30)
        {
            var weeks = Math.Max(1, (int)Math.Round(delta.TotalDays / 7));
            return weeks == 1 ? "před týdnem" : $"před {weeks} týdny";
        }

        if (delta.TotalDays < 365)
        {
            var months = Math.Max(1, (int)Math.Round(delta.TotalDays / 30));
            return months == 1 ? "před měsícem" : $"před {months} měsíci";
        }

        var years = Math.Max(1, (int)Math.Round(delta.TotalDays / 365));
        return years == 1 ? "před rokem" : $"před {years} lety";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => DependencyProperty.UnsetValue;
}

public sealed class BooleanToVisibilityConverterEx : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);

        if (value is bool flag)
        {
            flag = invert ? !flag : flag;
            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        if (value is bool? nullable && nullable.HasValue)
        {
            var flagValue = invert ? !nullable.Value : nullable.Value;
            return flagValue ? Visibility.Visible : Visibility.Collapsed;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is not Visibility visibility)
        {
            return DependencyProperty.UnsetValue;
        }

        var invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        var result = visibility == Visibility.Visible;
        return invert ? !result : result;
    }
}

public sealed class SelectionCountToStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int count)
        {
            return count switch
            {
                < 0 => string.Empty,
                0 => "Žádná položka není vybrána.",
                1 => "1 položka vybrána.",
                _ => $"Vybráno {count} položek."
            };
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => DependencyProperty.UnsetValue;
}

public sealed class SortDirectionToGlyphConverter : IValueConverter
{
    private static readonly IReadOnlyDictionary<ListSortDirection, string> Glyphs = new Dictionary<ListSortDirection, string>
    {
        [ListSortDirection.Ascending] = "\uE74A",
        [ListSortDirection.Descending] = "\uE74B",
    };

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is ListSortDirection direction && Glyphs.TryGetValue(direction, out var glyph))
        {
            return glyph;
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => DependencyProperty.UnsetValue;
}

public sealed class MimeToIconConverter : IValueConverter
{
    private static readonly IReadOnlyDictionary<string, string> MimeGlyphs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = "\uE8A5",
        ["image/png"] = "\uEB9F",
        ["image/jpeg"] = "\uEB9F",
        ["text/plain"] = "\uE8A5",
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = "\uE8A5",
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = "\uE8A5",
    };

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string mime && MimeGlyphs.TryGetValue(mime, out var glyph))
        {
            return glyph;
        }

        return "\uE7C3"; // Generic document glyph
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => DependencyProperty.UnsetValue;
}
