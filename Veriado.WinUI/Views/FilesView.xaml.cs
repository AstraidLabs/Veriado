using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.Infrastructure;
using Veriado.WinUI.ViewModels.Files;

namespace Veriado.WinUI.Views;

public sealed partial class FilesView : UserControl
{
    private readonly ILogger<FilesView> _logger;

    public FilesView(FilesGridViewModel viewModel, ILogger<FilesView> logger)
    {
        InitializeComponent();

        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public FilesGridViewModel ViewModel => (FilesGridViewModel)DataContext!;

    private async void Root_Loaded(object sender, RoutedEventArgs e)
    {
        await CommandForwarder.TryExecuteAsync(ViewModel.RefreshCommand, null, _logger)
            .ConfigureAwait(false);
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        await CommandForwarder.TryExecuteAsync(ViewModel.RefreshCommand, null, _logger)
            .ConfigureAwait(false);
    }

    private void OpenDetail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid id })
        {
            CommandForwarder.TryExecute(ViewModel.OpenDetailCommand, id, _logger);
        }
    }
}
