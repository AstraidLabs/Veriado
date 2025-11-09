using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Veriado.WinUI.Helpers;

public static class ValidityColors
{
    public static SolidColorBrush Ok { get; } = new(Color.FromArgb(0xFF, 0x16, 0xA3, 0x4A));

    public static SolidColorBrush Upcoming { get; } = new(Color.FromArgb(0xFF, 0xE6, 0xB8, 0x00));

    public static SolidColorBrush Soon { get; } = new(Color.FromArgb(0xFF, 0xF5, 0x7C, 0x00));

    public static SolidColorBrush Expired { get; } = new(Color.FromArgb(0xFF, 0xD9, 0x2F, 0x2F));

    public static SolidColorBrush Transparent { get; } = new(Color.FromArgb(0x00, 0x00, 0x00, 0x00));
}
