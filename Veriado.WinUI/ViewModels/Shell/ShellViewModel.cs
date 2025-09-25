using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.Services.Messages;
using Veriado.WinUI.ViewModels.Base;
using Veriado.WinUI.ViewModels.Files;
using Veriado.WinUI.ViewModels.Import;
using Veriado.WinUI.ViewModels.Search;
using Veriado.WinUI.Views;

namespace Veriado.WinUI.ViewModels;

public sealed partial class ShellViewModel : ViewModelBase, INavigationHost
{
    private readonly INavigationService _navigationService;
    private readonly FilesView _filesView;
    private readonly ImportView _importView;
    private readonly SettingsView _settingsView;

    [ObservableProperty]
    private object? currentContent;

    [ObservableProperty]
    private object? currentDetail;

    [ObservableProperty]
    private object? selectedNavItem;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private bool isInfoBarOpen;

    [ObservableProperty]
    private bool isNavOpen;

    public FilesGridViewModel Files { get; }

    public ImportViewModel Import { get; }

    public SearchOverlayViewModel Search { get; }

    public ShellViewModel(
        IMessenger messenger,
        IStatusService statusService,
        IDispatcherService dispatcher,
        IExceptionHandler exceptionHandler,
        INavigationService navigationService,
        FilesGridViewModel files,
        ImportViewModel import,
        SearchOverlayViewModel search,
        FilesView filesView,
        ImportView importView,
        SettingsView settingsView)
        : base(messenger, statusService, dispatcher, exceptionHandler)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        Files = files ?? throw new ArgumentNullException(nameof(files));
        Import = import ?? throw new ArgumentNullException(nameof(import));
        Search = search ?? throw new ArgumentNullException(nameof(search));
        _filesView = filesView ?? throw new ArgumentNullException(nameof(filesView));
        _importView = importView ?? throw new ArgumentNullException(nameof(importView));
        _settingsView = settingsView ?? throw new ArgumentNullException(nameof(settingsView));

        _navigationService.AttachHost(this);
        _navigationService.NavigateTo(_filesView);

        Messenger.Register<StatusChangedMessage>(this, (_, message) =>
        {
            HasError = message.HasError;
            StatusMessage = message.Message;
            IsInfoBarOpen = !string.IsNullOrEmpty(message.Message);
        });
    }

    partial void OnSelectedNavItemChanged(object? value)
    {
        if (value is NavigationViewItem item)
        {
            NavigateTo(item.Tag ?? item.Content);
        }
    }

    private void NavigateTo(object? tag)
    {
        switch (tag?.ToString())
        {
            case "Files":
                _navigationService.NavigateTo(_filesView);
                break;
            case "Import":
                _navigationService.NavigateTo(_importView);
                break;
            case "Settings":
                _navigationService.NavigateTo(_settingsView);
                break;
        }

        IsNavOpen = false;
    }
}
