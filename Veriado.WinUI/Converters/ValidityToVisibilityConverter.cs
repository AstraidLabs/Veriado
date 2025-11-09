using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Veriado.WinUI.ViewModels.Files;

namespace Veriado.WinUI.Converters;

public sealed class ValidityToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is ValidityState state && state != ValidityState.None)
        {
            return Visibility.Visible;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
