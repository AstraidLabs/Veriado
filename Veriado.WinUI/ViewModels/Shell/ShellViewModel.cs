using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Veriado.Services;
using Veriado.ViewModels.Base;

namespace Veriado.ViewModels.Shell;

public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;

    public ShellViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    [ObservableProperty]
    private string title = "Veriado";

    [ObservableProperty]
    private bool isBackEnabled;

    [RelayCommand]
    private void Initialize()
    {
        _navigationService.NavigateToFiles();
        UpdateBackButton();
    }

    [RelayCommand]
    private void Navigate(string? destination)
    {
        switch (destination)
        {
            case "Files":
                _navigationService.NavigateToFiles();
                Title = "Veriado – Soubory";
                break;
            case "Import":
                _navigationService.NavigateToImport();
                Title = "Veriado – Import";
                break;
        }

        UpdateBackButton();
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigationService.GoBack();
        UpdateBackButton();
    }

    private void UpdateBackButton()
        => IsBackEnabled = _navigationService.CanGoBack;
}
