using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Veriado.WinUI.ViewModels.Files;
using Windows.UI;

namespace Veriado.WinUI.Converters;

public sealed class ValidityStatusToBrushConverter : IValueConverter
{
    public Brush Ok { get; set; } = new SolidColorBrush(Color.FromArgb(0xFF, 0x16, 0xA3, 0x4A));

    public Brush Upcoming { get; set; } = new SolidColorBrush(Color.FromArgb(0xFF, 0xE6, 0xB8, 0x00));

    public Brush Soon { get; set; } = new SolidColorBrush(Color.FromArgb(0xFF, 0xF5, 0x7C, 0x00));

    public Brush Expired { get; set; } = new SolidColorBrush(Color.FromArgb(0xFF, 0xD9, 0x2F, 0x2F));

    public Brush None { get; set; } = new SolidColorBrush(Colors.Transparent);

    public object Convert(object value, Type targetType, object parameter, string language) =>
        (value as ValidityStatus? ?? ValidityStatus.None) switch
        {
            ValidityStatus.Ok => Ok,
            ValidityStatus.Upcoming => Upcoming,
            ValidityStatus.Soon => Soon,
            ValidityStatus.Expired => Expired,
            _ => None,
        };

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
