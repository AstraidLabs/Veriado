using System;
using System.Globalization;
using Microsoft.UI.Xaml.Data;
using Veriado.WinUI.ViewModels.Files;

namespace Veriado.WinUI.Converters;

public sealed class ValidityToTooltipConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not ValidityState state || state == ValidityState.None)
        {
            return null;
        }

        var context = ExtractContext(parameter);
        if (context.IssuedAt is not { } issuedAt || context.ValidUntil is not { } validUntil)
        {
            return null;
        }

        var culture = CultureInfo.CurrentCulture;
        return $"Platnost: {issuedAt.ToString("d", culture)} – {validUntil.ToString("d", culture)}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }

    private static ValidityTooltipContext ExtractContext(object parameter)
    {
        if (parameter is ValidityTooltipContext context)
        {
            return context;
        }

        return default;
    }
}
