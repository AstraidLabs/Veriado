using CommunityToolkit.WinUI.Converters;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Veriado.WinUI.Converters;

/// <summary>
/// Converts <see cref="KeyRoutedEventArgs"/> instances to <see cref="VirtualKey"/> values.
/// </summary>
public sealed class KeyRoutedEventArgsConverter : IEventArgsConverter
{
    /// <inheritdoc />
    public object? Convert(object value, object parameter)
    {
        if (value is KeyRoutedEventArgs args)
        {
            return args.Key;
        }

        return VirtualKey.None;
    }
}
