using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Veriado.WinUI.ViewModels.Import;
using Microsoft.UI.Xaml;

namespace Veriado.WinUI.Views.Import;

public sealed partial class ImportPage : Page
{
    public ImportPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<ImportPageViewModel>();
    }

    private ImportPageViewModel? ViewModel => DataContext as ImportPageViewModel;

    private void OnPageDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        if (e.DragUIOverride is not null)
        {
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.Caption = "Pustit pro výběr složky";
        }

        SetDragOverlayVisibility(true);
        e.Handled = true;
    }

    private async void OnPageDrop(object sender, DragEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        string? path = null;

        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            path = ResolvePathFromStorageItems(items);
        }
        else if (e.DataView.Contains(StandardDataFormats.Text))
        {
            var text = await e.DataView.GetTextAsync();
            path = text;
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            path = path.Trim();
            if (Directory.Exists(path))
            {
                ViewModel.SelectedFolder = path;
            }
            else if (File.Exists(path))
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    ViewModel.SelectedFolder = directory;
                }
            }
        }

        e.Handled = true;
        SetDragOverlayVisibility(false);
    }

    private void OnPageDragLeave(object sender, DragEventArgs e)
    {
        SetDragOverlayVisibility(false);
    }

    private void SetDragOverlayVisibility(bool isVisible)
    {
        if (DragOverlay is null)
        {
            return;
        }

        DragOverlay.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string? ResolvePathFromStorageItems(System.Collections.Generic.IReadOnlyList<IStorageItem> items)
    {
        if (items.Count == 0)
        {
            return null;
        }

        var folder = items.OfType<StorageFolder>().FirstOrDefault();
        if (folder is not null)
        {
            return folder.Path;
        }

        var file = items.OfType<StorageFile>().FirstOrDefault();
        return file?.Path;
    }

    private async void OnEnterAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel?.RunImportCommand.CanExecute(null) == true)
        {
            await ViewModel.RunImportCommand.ExecuteAsync(null);
            args.Handled = true;
        }
    }

    private void OnEscapeAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel?.StopImportCommand.CanExecute(null) == true)
        {
            ViewModel.StopImportCommand.Execute(null);
            args.Handled = true;
        }
    }

    private async void OnOpenAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel?.PickFolderCommand.CanExecute(null) == true)
        {
            await ViewModel.PickFolderCommand.ExecuteAsync(null);
            args.Handled = true;
        }
    }

}
