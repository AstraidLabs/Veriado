using System;
using CommunityToolkit.WinUI.UI;
using Microsoft.UI.Xaml.Controls;

namespace Veriado.WinUI.Converters;

/// <summary>
/// Converts <see cref="NavigationViewSelectionChangedEventArgs"/> to the associated navigation tag.
/// </summary>
public sealed class NavigationSelectionConverter : IEventArgsConverter
{
    /// <inheritdoc />
    public object? Convert(object value, object parameter)
    {
        if (value is NavigationViewSelectionChangedEventArgs args)
        {
            return args.SelectedItemContainer?.Tag?.ToString();
        }

        return null;
    }
}
