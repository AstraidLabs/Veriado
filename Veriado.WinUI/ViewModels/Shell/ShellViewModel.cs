using System;
using System.ComponentModel;
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

public sealed partial class ShellViewModel : ViewModelBase
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

    public FilesGridViewModel Files { get; }

    public ImportViewModel Import { get; }

    public SearchOverlayViewModel Search { get; }

    public ShellViewModel(
        IMessenger messenger,
        IStatusService statusService,
        INavigationService navigationService,
        FilesGridViewModel files,
        ImportViewModel import,
        SearchOverlayViewModel search,
        FilesView filesView,
        ImportView importView,
        SettingsView settingsView)
        : base(messenger, statusService)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        Files = files ?? throw new ArgumentNullException(nameof(files));
        Import = import ?? throw new ArgumentNullException(nameof(import));
        Search = search ?? throw new ArgumentNullException(nameof(search));
        _filesView = filesView ?? throw new ArgumentNullException(nameof(filesView));
        _importView = importView ?? throw new ArgumentNullException(nameof(importView));
        _settingsView = settingsView ?? throw new ArgumentNullException(nameof(settingsView));

        if (_navigationService is INotifyPropertyChanged notifier)
        {
            notifier.PropertyChanged += OnNavigationServicePropertyChanged;
        }

        _navigationService.NavigateToContent(_filesView);
        UpdateNavigationState();

        Messenger.Register<StatusChangedMessage>(this, (_, message) =>
        {
            HasError = message.HasError;
            StatusMessage = message.Message;
        });
    }

    protected override bool BroadcastStatusChanges => false;

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
                _navigationService.NavigateToContent(_filesView);
                break;
            case "Import":
                _navigationService.NavigateToContent(_importView);
                break;
            case "Settings":
                _navigationService.NavigateToContent(_settingsView);
                break;
        }

        UpdateNavigationState();
    }

    private void UpdateNavigationState()
    {
        CurrentContent = _navigationService.CurrentContent;
        CurrentDetail = _navigationService.CurrentDetail;
    }

    private void OnNavigationServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(INavigationService.CurrentContent) or nameof(INavigationService.CurrentDetail))
        {
            UpdateNavigationState();
        }
    }
}
