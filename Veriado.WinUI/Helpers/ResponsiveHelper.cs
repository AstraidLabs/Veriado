using Microsoft.UI.Xaml;

namespace Veriado.WinUI.Helpers;

public static class ResponsiveHelper
{
    public static double GetEffectiveWidth(FrameworkElement? element)
    {
        if (element is null)
        {
            return 0d;
        }

        if (element.ActualWidth > 0)
        {
            return element.ActualWidth;
        }

        var xamlRoot = element.XamlRoot;
        if (xamlRoot is not null && xamlRoot.Size.Width > 0)
        {
            return xamlRoot.Size.Width;
        }

        return element.Width > 0 ? element.Width : 0d;
    }
}
