using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Veriado.WinUI.ViewModels;
using Veriado.WinUI.Views;

namespace Veriado;

public sealed partial class MainWindow : Window
{
    public ShellViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();
        Title = "Veriado";
        ViewModel = AppHost.Services.GetRequiredService<ShellViewModel>();
        DataContext = ViewModel;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        var firstItem = ShellNavigationView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault();
        ShellNavigationView.SelectedItem = firstItem;
        RootFrame.Navigate(typeof(FilesPage));
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            return;
        }

        var tag = args.SelectedItemContainer?.Tag?.ToString();
        Navigate(tag);
    }

    private void Navigate(string? tag)
    {
        switch (tag)
        {
            case "Files":
                RootFrame.Navigate(typeof(FilesPage));
                break;
            case "Import":
                RootFrame.Navigate(typeof(ImportPage));
                break;
        }
    }

    private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        throw new InvalidOperationException($"Navigace selhala: {e.SourcePageType}");
    }
}
