using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.WinUI.Messages;
using Veriado.WinUI.Services;

namespace Veriado.WinUI.ViewModels;

/// <summary>
/// Aggregates the primary feature view models exposed by the shell.
/// </summary>
public sealed partial class ShellViewModel : ViewModelBase, IRecipient<ImportCompletedMessage>
{
    private readonly INavigationService _navigationService;
    private readonly Lazy<FilesGridViewModel> _files;
    private readonly Lazy<FileDetailViewModel> _detail;
    private readonly Lazy<ImportViewModel> _import;

    public ShellViewModel(
        INavigationService navigationService,
        Func<FilesGridViewModel> filesFactory,
        Func<FileDetailViewModel> detailFactory,
        Func<ImportViewModel> importFactory,
        IMessenger messenger)
        : base(messenger)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        ArgumentNullException.ThrowIfNull(filesFactory);
        ArgumentNullException.ThrowIfNull(detailFactory);
        ArgumentNullException.ThrowIfNull(importFactory);

        _files = new Lazy<FilesGridViewModel>(filesFactory);
        _detail = new Lazy<FileDetailViewModel>(detailFactory);
        _import = new Lazy<ImportViewModel>(importFactory);

        Messenger.Register<ShellViewModel, ImportCompletedMessage>(this, static (recipient, message) => recipient.Receive(message));
    }

    /// <summary>
    /// Gets the grid view model that exposes the paged catalogue.
    /// </summary>
    public FilesGridViewModel Files => _files.Value;

    /// <summary>
    /// Gets the detail view model used to present individual file information.
    /// </summary>
    public FileDetailViewModel Detail => _detail.Value;

    /// <summary>
    /// Gets the import workflow view model.
    /// </summary>
    public ImportViewModel Import => _import.Value;

    [ObservableProperty]
    private string selectedNavigationTag = NavigationItemTags.Files;

    /// <summary>
    /// Initializes the shell by navigating to the default page.
    /// </summary>
    [RelayCommand]
    private void Initialize()
    {
        SelectedNavigationTag = NavigationItemTags.Files;
        _navigationService.NavigateToFiles();
    }

    /// <summary>
    /// Navigates to the page associated with the supplied navigation tag.
    /// </summary>
    [RelayCommand]
    private void Navigate(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        SelectedNavigationTag = tag;
        switch (tag)
        {
            case NavigationItemTags.Files:
                _navigationService.NavigateToFiles();
                break;
            case NavigationItemTags.Import:
                _navigationService.NavigateToImport();
                break;
        }
    }

    /// <inheritdoc />
    public void Receive(ImportCompletedMessage message)
    {
        StatusMessage = $"Import dokončen: {message.Succeeded}/{message.Total}.";
        IsInfoBarOpen = true;
    }

    private static class NavigationItemTags
    {
        public const string Files = "Files";
        public const string Import = "Import";
    }
}
