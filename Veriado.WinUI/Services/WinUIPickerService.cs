using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Veriado.Presentation.Services;

namespace Veriado.WinUI.Services;

/// <summary>
/// WinUI implementation of <see cref="IPickerService"/> using the platform pickers.
/// </summary>
public sealed class WinUIPickerService : IPickerService
{
    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken)
    {
        var window = App.MainWindowInstance ?? throw new InvalidOperationException("Main window is not available.");
        var hwnd = WindowNative.GetWindowHandle(window);

        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync().AsTask(cancellationToken);
        return folder?.Path;
    }

    public async Task<PickedFile?> PickFileAsync(CancellationToken cancellationToken)
    {
        var window = App.MainWindowInstance ?? throw new InvalidOperationException("Main window is not available.");
        var hwnd = WindowNative.GetWindowHandle(window);

        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync().AsTask(cancellationToken);
        if (file is null)
        {
            return null;
        }

        await using var stream = await file.OpenStreamForReadAsync();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        return new PickedFile(file.Name, buffer.ToArray(), file.ContentType);
    }
}
