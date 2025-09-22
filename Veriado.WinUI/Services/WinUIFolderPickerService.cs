// BEGIN CHANGE Veriado.WinUI/Services/WinUIFolderPickerService.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Veriado.WinUI.Services;

/// <summary>
/// WinUI implementation of <see cref="IPickerService"/> using the platform folder picker.
/// </summary>
public sealed class WinUIFolderPickerService : IPickerService
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
}
// END CHANGE Veriado.WinUI/Services/WinUIFolderPickerService.cs
