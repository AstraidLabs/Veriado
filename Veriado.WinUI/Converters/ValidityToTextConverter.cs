using System;
using Microsoft.UI.Xaml.Data;
using Veriado.WinUI.ViewModels.Files;

namespace Veriado.WinUI.Converters;

public sealed class ValidityToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not ValidityState state)
        {
            return string.Empty;
        }

        return state switch
        {
            ValidityState.None => string.Empty,
            ValidityState.Expired => "Platnost skončila",
            ValidityState.ExpiringToday => "Dnes končí",
            ValidityState.ExpiringSoon or ValidityState.ExpiringLater or ValidityState.LongTerm => FormatRemaining(parameter),
            _ => string.Empty,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }

    private static string FormatRemaining(object parameter)
    {
        if (parameter is int days)
        {
            return $"Zbývá {days} dní";
        }

        return string.Empty;
    }
}
