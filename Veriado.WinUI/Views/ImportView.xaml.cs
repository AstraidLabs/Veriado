using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.Infrastructure;
using Veriado.WinUI.ViewModels.Import;

namespace Veriado.WinUI.Views;

public sealed partial class ImportView : UserControl
{
    private readonly ILogger<ImportView> _logger;

    public ImportView(ImportViewModel viewModel, ILogger<ImportView> logger)
    {
        InitializeComponent();

        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ImportViewModel ViewModel => (ImportViewModel)DataContext!;

    private async void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        await CommandForwarder.TryExecuteAsync(ViewModel.BrowseFolderCommand, null, _logger)
            .ConfigureAwait(false);
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        await CommandForwarder.TryExecuteAsync(ViewModel.ImportCommand, null, _logger)
            .ConfigureAwait(false);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CommandForwarder.TryExecute(ViewModel.CancelImportCommand, null, _logger);
    }
}
