using Veriado.WinUI.Navigation;

namespace Veriado.WinUI.ViewModels.Shell;

public partial class MainShellViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private bool _isInitialized;

    public MainShellViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
    }

    [ObservableProperty]
    private bool isNavOpen;

    [ObservableProperty]
    private PageId currentPage = PageId.Files;

    [RelayCommand]
    private void ToggleNav()
    {
        IsNavOpen = !IsNavOpen;
    }

    [RelayCommand]
    private void CloseNav()
    {
        IsNavOpen = false;
    }

    public void NavigateToTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        var normalized = tag.Trim().ToLowerInvariant();
        var target = normalized switch
        {
            "files" => PageId.Files,
            "import" => PageId.Import,
            "settings" => PageId.Settings,
            _ => CurrentPage,
        };

        NavigateInternal(target);
    }

    public void Initialize()
    {
        if (_isInitialized)
        {
            return;
        }

        NavigateInternal(PageId.Files, closePane: false, forceNavigation: true);
    }

    public void Navigate(PageId pageId)
    {
        NavigateInternal(pageId);
    }

    private void NavigateInternal(PageId pageId, bool closePane = true, bool forceNavigation = false)
    {
        var isDifferentPage = CurrentPage != pageId;

        if (isDifferentPage)
        {
            CurrentPage = pageId;
        }

        if (forceNavigation || isDifferentPage || !_isInitialized)
        {
            _navigationService.Navigate(pageId);
            _isInitialized = true;
        }

        if (closePane)
        {
            CloseNav();
        }
    }
}
