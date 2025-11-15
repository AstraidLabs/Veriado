using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Veriado.Contracts.Files;
using Veriado.WinUI.Resources;

namespace Veriado.WinUI.Converters;

public sealed class ValidityStatusToForegroundBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = value is ValidityStatus castStatus ? castStatus : ValidityStatus.None;
        return status switch
        {
            ValidityStatus.Expired => AppColorPalette.ValidityLightForegroundBrush,
            ValidityStatus.ExpiringToday => AppColorPalette.ValidityLightForegroundBrush,
            ValidityStatus.ExpiringSoon => AppColorPalette.ValidityLightForegroundBrush,
            ValidityStatus.ExpiringLater => AppColorPalette.ValidityDarkForegroundBrush,
            ValidityStatus.LongTerm => AppColorPalette.ValidityDarkForegroundBrush,
            _ => AppColorPalette.ValidityDarkForegroundBrush,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
