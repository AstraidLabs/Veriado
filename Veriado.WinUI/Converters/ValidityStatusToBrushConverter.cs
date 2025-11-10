using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Veriado.Contracts.Files;
using Veriado.WinUI.Helpers;

namespace Veriado.WinUI.Converters;

public sealed class ValidityStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = value is ValidityStatus castStatus ? castStatus : ValidityStatus.None;
        return status switch
        {
            ValidityStatus.LongTerm => ValidityColors.LongTerm,
            ValidityStatus.ExpiringLater => ValidityColors.ExpiringLater,
            ValidityStatus.ExpiringSoon => ValidityColors.ExpiringSoon,
            ValidityStatus.ExpiringToday => ValidityColors.Expired,
            ValidityStatus.Expired => ValidityColors.Expired,
            _ => ValidityColors.Transparent,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
