using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Veriado.Contracts.Files;
using Veriado.WinUI.Helpers;

namespace Veriado.WinUI.Converters;

public sealed class ValidityStatusToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = value is ValidityStatus castStatus ? castStatus : ValidityStatus.None;
        var glyph = ValidityGlyphProvider.GetGlyph(status);

        if (parameter is string parameterText
            && string.Equals(parameterText, "Visibility", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrEmpty(glyph) ? Visibility.Collapsed : Visibility.Visible;
        }

        return glyph;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
