using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Veriado.WinUI.Helpers;
using Veriado.WinUI.ViewModels.Files;

namespace Veriado.WinUI.Converters;

public sealed class ValidityStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = value is ValidityStatus castStatus ? castStatus : ValidityStatus.None;
        return status switch
        {
            ValidityStatus.Ok => ValidityColors.Ok,
            ValidityStatus.Upcoming => ValidityColors.Upcoming,
            ValidityStatus.Soon => ValidityColors.Soon,
            ValidityStatus.Expired => ValidityColors.Expired,
            _ => ValidityColors.Transparent,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
