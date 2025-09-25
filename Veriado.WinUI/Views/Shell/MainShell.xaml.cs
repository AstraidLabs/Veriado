using System;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Veriado.WinUI.Navigation;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.ViewModels.Shell;

namespace Veriado.WinUI.Views.Shell;

public sealed partial class MainShell : Window, INavigationHost
{
    private readonly MainShellViewModel _viewModel;
    private readonly INavigationService _navigationService;

    public MainShell(MainShellViewModel viewModel, INavigationService navigationService)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));

        RootGrid.DataContext = _viewModel;
        _navigationService.AttachHost(this);

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        RootGrid.Loaded += OnLoaded;
        Closed += OnClosed;
    }

    public object? CurrentContent
    {
        get => ContentHost.Content;
        set => ContentHost.Content = value;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= OnLoaded;
        _viewModel.Initialize();
        UpdateNavigationSelection(_viewModel.CurrentPage);
    }

    private void OnClosed(object sender, WindowEventArgs e)
    {
        Closed -= OnClosed;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnNavigationViewItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        var tag = args.InvokedItemContainer?.Tag as string;
        _viewModel.NavigateToTag(tag);
    }

    private void OnOverlayTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_viewModel.CloseNavCommand.CanExecute(null))
        {
            _viewModel.CloseNavCommand.Execute(null);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainShellViewModel.CurrentPage))
        {
            UpdateNavigationSelection(_viewModel.CurrentPage);
        }
    }

    private void UpdateNavigationSelection(PageId pageId)
    {
        var tag = pageId switch
        {
            PageId.Files => "files",
            PageId.Import => "import",
            PageId.Settings => "settings",
            _ => string.Empty,
        };

        var match = RootNavigation.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag is string itemTag && itemTag.Equals(tag, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            RootNavigation.SelectedItem = match;
        }
    }
}
