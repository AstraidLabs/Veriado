using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Veriado.Infrastructure;
using Veriado.WinUI.ViewModels.Import;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

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

    private void RootPanel_DragOver(object sender, DragEventArgs e)
    {
        if (e?.DataView?.Contains(StandardDataFormats.StorageItems) == true)
        {
            e.AcceptedOperation = DataPackageOperation.Link;
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;
            e.DragUIOverride.Caption = "Pustit pro výběr složky";
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
            e.DragUIOverride.IsGlyphVisible = false;
        }
    }

    private async void RootPanel_Drop(object sender, DragEventArgs e)
    {
        if (e?.DataView?.Contains(StandardDataFormats.StorageItems) != true)
        {
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.Count == 0)
            {
                return;
            }

            var folder = await ResolveFolderAsync(items);
            if (folder is null)
            {
                _logger.LogWarning("No folder could be resolved from the dropped items.");
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Link;
            CommandForwarder.TryExecute(ViewModel.UseFolderPathCommand, folder.Path, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process dropped folder.");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static async Task<StorageFolder?> ResolveFolderAsync(IReadOnlyList<IStorageItem> items)
    {
        var folder = items.OfType<StorageFolder>().FirstOrDefault();
        if (folder is not null)
        {
            return folder;
        }

        foreach (var item in items)
        {
            if (item is StorageFile file)
            {
                try
                {
                    return await file.GetParentAsync();
                }
                catch
                {
                    // Ignored – we'll try the remaining items.
                }
            }
        }

        return null;
    }
}
