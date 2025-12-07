using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Windowing;
using Veriado.WinUI.Navigation;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.ViewModels.Shell;
using WinRT.Interop;

namespace Veriado.WinUI.Views.Shell;

public sealed partial class MainShell : Window, INavigationHost
{
    private readonly MainShellViewModel _viewModel;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private AppWindow? _appWindow;

    public MainShell(MainShellViewModel viewModel, INavigationService navigationService, IDialogService dialogService)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        RootNavigation.DataContext = _viewModel;
        _navigationService.AttachHost(this);

        InitializeWindowing();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        RootNavigation.Loaded += OnLoaded;
        Closed += OnClosed;
    }

    public object? CurrentContent
    {
        get => ContentHost.Content;
        set => ContentHost.Content = value;
    }

    public void ShowShell()
    {
        _appWindow?.Show();
        Activate();
    }

    public void HideShell()
    {
        _appWindow?.Hide();
        
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RootNavigation.Loaded -= OnLoaded;
        _viewModel.Initialize();
        UpdateNavigationSelection(_viewModel.CurrentPage);

        if (_appWindow is null)
        {
            InitializeWindowing();
        }
    }

    private void OnClosed(object sender, WindowEventArgs e)
    {
        Closed -= OnClosed;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        if (_appWindow is not null)
        {
            _appWindow.Closing -= OnAppWindowClosing;
            _appWindow = null;
        }
    }

    private void OnNavigationViewItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        var tag = args.InvokedItemContainer?.Tag as string;
        _viewModel.NavigateToTag(tag);
    }

    private void InitializeWindowing()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow.Closing += OnAppWindowClosing;
        }
        catch
        {
            _appWindow = null;
        }
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (App.Current.IsShuttingDown)
        {
            args.Cancel = false;

            if (_appWindow is not null)
            {
                _appWindow.Closing -= OnAppWindowClosing;
            }

            return;
        }

        args.Cancel = true;

        App.Current.HideMainWindow();
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
