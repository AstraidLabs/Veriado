using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Veriado.WinUI.Resources;
using Veriado.WinUI.ViewModels.Files;

namespace Veriado.WinUI.Converters;

public sealed class ValidityToForegroundBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not ValidityState state)
        {
            return AppColorPalette.ValidityDarkForegroundBrush;
        }

        return state switch
        {
            ValidityState.Expired => AppColorPalette.ValidityLightForegroundBrush,
            ValidityState.ExpiringToday => AppColorPalette.ValidityLightForegroundBrush,
            ValidityState.ExpiringSoon => AppColorPalette.ValidityLightForegroundBrush,
            _ => AppColorPalette.ValidityDarkForegroundBrush,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
