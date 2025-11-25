using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Veriado.WinUI.ViewModels.Import;

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
            path = ExtractPathFromText(text);
        }
        else if (e.DataView.Contains(StandardDataFormats.Uri))
        {
            var uri = await e.DataView.GetUriAsync();
            path = uri?.LocalPath;
        }

        path = NormalizeDroppedPath(path);

        if (!string.IsNullOrWhiteSpace(path))
        {
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

    private static string? ResolvePathFromStorageItems(IReadOnlyList<IStorageItem> items)
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

    private static string? ExtractPathFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var candidate in text
                     .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                     .Select(static line => line.Trim()))
        {
            if (!string.IsNullOrEmpty(candidate))
            {
                return candidate;
            }
        }

        return text.Trim();
    }

    private static string? NormalizeDroppedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var sanitized = path.Trim();
        if (sanitized.Length >= 2 && sanitized.StartsWith('"') && sanitized.EndsWith('"'))
        {
            sanitized = sanitized[1..^1];
        }

        if (Uri.TryCreate(sanitized, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            sanitized = uri.LocalPath;
        }

        try
        {
            sanitized = Path.GetFullPath(sanitized);
        }
        catch (Exception)
        {
            // Ignore invalid paths and fall back to the original sanitized string.
        }

        sanitized = Path.TrimEndingDirectorySeparator(sanitized);

        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    private async void OnEnterAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel?.RunImportCommand.CanExecute(null) == true)
        {
            await ViewModel.RunImportCommand.ExecuteAsync(null);
            args.Handled = true;
        }
    }

    private async void OnEscapeAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel?.StopImportCommand.CanExecute(null) == true)
        {
            await ViewModel.StopImportCommand.ExecuteAsync(null);
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
