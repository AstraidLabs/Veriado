using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Veriado.WinUI.Navigation;

namespace Veriado.WinUI.ViewModels.Shell;

public partial class MainShellViewModel : ObservableObject
{
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

        CurrentPage = target;
        CloseNav();
    }
}
