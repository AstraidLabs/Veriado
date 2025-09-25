using System;
using System.ComponentModel;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Veriado.WinUI.Navigation;
using Veriado.WinUI.ViewModels.Shell;

namespace Veriado.WinUI.Views.Shell;

public sealed partial class MainShell : Window
{
    private readonly IServiceProvider _serviceProvider;
    private MainShellViewModel? _viewModel;

    public MainShell()
    {
        InitializeComponent();
        _serviceProvider = App.Services;
    }

    public void Initialize(MainShellViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        LoadPage(_viewModel.CurrentPage);
    }

    private void OnNavigationViewItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (_viewModel is null)
        {
            return;
        }

        var tag = args.InvokedItemContainer?.Tag as string;
        _viewModel.NavigateToTag(tag);
    }

    private void OnOverlayTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_viewModel?.CloseNavCommand.CanExecute(null) == true)
        {
            _viewModel.CloseNavCommand.Execute(null);
        }
    }

    private void OnToggleNavAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_viewModel?.ToggleNavCommand.CanExecute(null) == true)
        {
            _viewModel.ToggleNavCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void OnCloseNavAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_viewModel?.CloseNavCommand.CanExecute(null) == true)
        {
            _viewModel.CloseNavCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (e.PropertyName == nameof(MainShellViewModel.CurrentPage))
        {
            LoadPage(_viewModel.CurrentPage);
        }
    }

    private void LoadPage(PageId pageId)
    {
        var content = pageId switch
        {
            PageId.Files => _serviceProvider.GetRequiredService<Views.Files.FilesPage>(),
            PageId.Import => _serviceProvider.GetRequiredService<Views.Import.ImportPage>(),
            PageId.Settings => _serviceProvider.GetRequiredService<Views.Settings.SettingsPage>(),
            _ => throw new ArgumentOutOfRangeException(nameof(pageId), pageId, null),
        };

        ContentHost.Content = content;
        UpdateNavigationSelection(pageId);
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
