using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.ViewModels.Files;
using Veriado.WinUI.ViewModels.Import;
using Veriado.WinUI.Views;

namespace Veriado.WinUI.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly IMessenger _messenger;

    [ObservableProperty]
    private object? currentContent;

    [ObservableProperty]
    private object? currentDetail;

    [ObservableProperty]
    private object? selectedNavItem;

    [ObservableProperty]
    private bool isInfoBarOpen;

    [ObservableProperty]
    private string? statusMessage;

    public FilesGridViewModel Files { get; }

    public ImportViewModel Import { get; }

    public SearchOverlayViewModel Search { get; }

    public ShellViewModel(
        IServiceProvider services,
        FilesGridViewModel files,
        ImportViewModel import,
        SearchOverlayViewModel search,
        IMessenger messenger)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        Files = files ?? throw new ArgumentNullException(nameof(files));
        Import = import ?? throw new ArgumentNullException(nameof(import));
        Search = search ?? throw new ArgumentNullException(nameof(search));
        _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));

        CurrentContent = _services.GetRequiredService<FilesView>();
        CurrentDetail = null;

        _messenger.Register<ShellViewModel, FileSelectedMessage>(this, static (recipient, message) =>
        {
            recipient.ShowFileDetail(message.FileId);
        });
    }

    partial void OnSelectedNavItemChanged(object? value)
    {
        if (value is NavigationViewItem item)
        {
            Navigate(item.Tag ?? item.Content);
        }
    }

    [RelayCommand]
    private void Navigate(object? tag)
    {
        switch (tag?.ToString())
        {
            case "Files":
                CurrentContent = _services.GetRequiredService<FilesView>();
                break;
            case "Import":
                CurrentContent = _services.GetRequiredService<ImportView>();
                break;
            case "Settings":
                CurrentContent = _services.GetRequiredService<SettingsView>();
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
        view.ViewModel.LoadCommand.Execute(fileId);
        CurrentDetail = view;
    }
}

public sealed record FileSelectedMessage(Guid FileId);
