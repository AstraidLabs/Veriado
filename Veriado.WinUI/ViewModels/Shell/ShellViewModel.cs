using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.ViewModels.Base;
using Veriado.WinUI.ViewModels.Files;
using Veriado.WinUI.ViewModels.Import;
using Veriado.WinUI.ViewModels.Messages;
using Veriado.WinUI.ViewModels.Search;
using Veriado.WinUI.Views;

namespace Veriado.WinUI.ViewModels;

public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly IServiceProvider _services;

    [ObservableProperty]
    private object? currentContent;

    [ObservableProperty]
    private object? currentDetail;

    [ObservableProperty]
    private object? selectedNavItem;

    [ObservableProperty]
    private bool isInfoBarOpen;

    public FilesGridViewModel Files { get; }

    public ImportViewModel Import { get; }

    public SearchOverlayViewModel Search { get; }

    public ShellViewModel(
        IServiceProvider services,
        FilesGridViewModel files,
        ImportViewModel import,
        SearchOverlayViewModel search,
        IMessenger messenger)
        : base(messenger)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        Files = files ?? throw new ArgumentNullException(nameof(files));
        Import = import ?? throw new ArgumentNullException(nameof(import));
        Search = search ?? throw new ArgumentNullException(nameof(search));

        CurrentContent = _services.GetRequiredService<FilesView>();
        CurrentDetail = null;

        Messenger.Register<StatusChangedMessage>(this, (_, message) =>
        {
            HasError = message.IsError;
            StatusMessage = message.Text;
            IsInfoBarOpen = !string.IsNullOrWhiteSpace(message.Text);
        });

        Messenger.Register<OpenFileDetailMessage>(this, (_, message) =>
        {
            ShowFileDetail(message.FileId);
        });
    }

    protected override bool BroadcastStatusChanges => false;

    partial void OnStatusMessageChanged(string? value)
    {
        IsInfoBarOpen = !string.IsNullOrWhiteSpace(value);
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
                CurrentContent = _services.GetRequiredService<FilesView>();
                CurrentDetail = null;
                break;
            case "Import":
                CurrentContent = _services.GetRequiredService<ImportView>();
                CurrentDetail = null;
                break;
            case "Settings":
                CurrentContent = _services.GetRequiredService<SettingsView>();
                CurrentDetail = null;
                break;
        }
    }

    private void ShowFileDetail(Guid fileId)
    {
        if (fileId == Guid.Empty)
        {
            return;
        }

        var view = _services.GetRequiredService<FileDetailView>();
        CurrentDetail = view;

        if (view.DataContext is FileDetailViewModel detailViewModel)
        {
            _ = detailViewModel.LoadCommand.ExecuteAsync(fileId);
        }
    }
}
