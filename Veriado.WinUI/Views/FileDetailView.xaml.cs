using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Veriado.Infrastructure;
using Veriado.WinUI.ViewModels.Files;

namespace Veriado.WinUI.Views;

public sealed partial class FileDetailView : UserControl
{
    private readonly ILogger<FileDetailView> _logger;

    public FileDetailView(FileDetailViewModel viewModel, ILogger<FileDetailView> logger)
    {
        InitializeComponent();

        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public FileDetailViewModel ViewModel => (FileDetailViewModel)DataContext!;

    private async void RenameButton_Click(object sender, RoutedEventArgs e)
    {
        await CommandForwarder.TryExecuteAsync(ViewModel.RenameCommand, null, _logger)
            .ConfigureAwait(false);
    }

    private async void ReadOnlySwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch)
        {
            await CommandForwarder.TryExecuteAsync(ViewModel.SetReadOnlyCommand, toggleSwitch.IsOn, _logger)
                .ConfigureAwait(false);
        }
    }

    private async void ApplyValidityButton_Click(object sender, RoutedEventArgs e)
    {
        await CommandForwarder.TryExecuteAsync(ViewModel.ApplyValidityCommand, null, _logger)
            .ConfigureAwait(false);
    }

    private async void ClearValidityButton_Click(object sender, RoutedEventArgs e)
    {
        await CommandForwarder.TryExecuteAsync(ViewModel.ClearValidityCommand, null, _logger)
            .ConfigureAwait(false);
    }

    private async void UpdateMetadataButton_Click(object sender, RoutedEventArgs e)
    {
        await CommandForwarder.TryExecuteAsync(ViewModel.UpdateMetadataCommand, null, _logger)
            .ConfigureAwait(false);
    }

    private async void CopyIdButton_Click(object sender, RoutedEventArgs e)
    {
        await CommandForwarder.TryExecuteAsync(ViewModel.CopyIdCommand, null, _logger)
            .ConfigureAwait(false);
    }

    private async void CopySnippetButton_Click(object sender, RoutedEventArgs e)
    {
        await CommandForwarder.TryExecuteAsync(ViewModel.CopySnippetCommand, null, _logger)
            .ConfigureAwait(false);
    }

    private async void ShareSnippetButton_Click(object sender, RoutedEventArgs e)
    {
        await CommandForwarder.TryExecuteAsync(ViewModel.ShareSnippetCommand, null, _logger)
            .ConfigureAwait(false);
    }
}
