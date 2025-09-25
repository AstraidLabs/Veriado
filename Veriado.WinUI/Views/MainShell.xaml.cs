using System;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Veriado.WinUI.Infrastructure;
using Veriado.WinUI.Services.Messages;
using Veriado.WinUI.ViewModels;

namespace Veriado.WinUI.Views;

public sealed partial class MainShell : UserControl
{
    public MainShell()
    {
        InitializeComponent();
    }

    public ShellViewModel ViewModel => (ShellViewModel)DataContext!;

    private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        WeakReferenceMessenger.Default.Send(new FocusSearchRequestedMessage());
        args.Handled = true;
    }

    private void ToggleNavigation_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (DataContext is ShellViewModel vm)
        {
            vm.IsNavOpen = !vm.IsNavOpen;
        }

        args.Handled = true;
    }

    private void CloseNavigation_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (DataContext is ShellViewModel vm && vm.IsNavOpen)
        {
            vm.IsNavOpen = false;
            args.Handled = true;
        }
    }

    private async void RefreshAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (DataContext is ShellViewModel vm)
        {
            await CommandForwarder.TryExecuteAsync(vm.Files.RefreshCommand, null);
            args.Handled = true;
        }
    }

    private void NavigationOverlay_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement overlay || DataContext is not ShellViewModel vm)
        {
            return;
        }

        if (e.OriginalSource is FrameworkElement element && IsInsideNavigationView(element))
        {
            return;
        }

        vm.IsNavOpen = false;
        e.Handled = true;
    }

    private static bool IsInsideNavigationView(FrameworkElement element)
    {
        var current = element;
        while (current is not null)
        {
            if (current is NavigationView)
            {
                return true;
            }

            current = current.Parent as FrameworkElement;
        }

        return false;
    }

    private void ForwardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Allow child controls to handle these accelerators.
        args.Handled = false;
    }
}
