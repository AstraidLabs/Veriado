using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Veriado.WinUI.Resources;
using Veriado.WinUI.ViewModels.Files;

namespace Veriado.WinUI.Converters;

public sealed class ValidityToBackgroundBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not ValidityState state)
        {
            return AppColorPalette.ValidityLongTermBackgroundBrush;
        }

        return state switch
        {
            ValidityState.Expired => AppColorPalette.ValidityExpiredBackgroundBrush,
            ValidityState.ExpiringToday => AppColorPalette.ValidityExpiredBackgroundBrush,
            ValidityState.ExpiringSoon => AppColorPalette.ValidityExpiringSoonBackgroundBrush,
            ValidityState.ExpiringLater => AppColorPalette.ValidityExpiringLaterBackgroundBrush,
            ValidityState.LongTerm => AppColorPalette.ValidityLongTermBackgroundBrush,
            _ => AppColorPalette.ValidityLongTermBackgroundBrush,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
