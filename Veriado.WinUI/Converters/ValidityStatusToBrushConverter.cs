using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Veriado.Contracts.Files;
using Veriado.WinUI.Resources;

namespace Veriado.WinUI.Converters;

public sealed class ValidityStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = value is ValidityStatus castStatus ? castStatus : ValidityStatus.None;
        return status switch
        {
            ValidityStatus.LongTerm => AppColorPalette.ValidityLongTermBackgroundBrush,
            ValidityStatus.ExpiringLater => AppColorPalette.ValidityExpiringLaterBackgroundBrush,
            ValidityStatus.ExpiringSoon => AppColorPalette.ValidityExpiringSoonBackgroundBrush,
            ValidityStatus.ExpiringToday => AppColorPalette.ValidityExpiredBackgroundBrush,
            ValidityStatus.Expired => AppColorPalette.ValidityExpiredBackgroundBrush,
            _ => AppColorPalette.ValidityLongTermBackgroundBrush,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
