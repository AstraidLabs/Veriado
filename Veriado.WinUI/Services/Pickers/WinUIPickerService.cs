using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Veriado.WinUI.Services.Pickers;

public sealed class WinUIPickerService : IPickerService
{
    private static IntPtr GetWindowHandle()
    {
        if (Application.Current is not App app || app.MainWindow is null)
        {
            throw new InvalidOperationException("Main window is not available.");
        }

        return WindowNative.GetWindowHandle(app.MainWindow);
    }

    public async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, GetWindowHandle());
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public async Task<string[]?> PickFilesAsync(string[]? extensions = null)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, GetWindowHandle());

        if (extensions is { Length: > 0 })
        {
            foreach (var extension in extensions)
            {
                if (!string.IsNullOrWhiteSpace(extension))
                {
                    picker.FileTypeFilter.Add(extension);
                }
            }
        }
        else
        {
            picker.FileTypeFilter.Add("*");
        }

        var files = await picker.PickMultipleFilesAsync();
        return files?.Select(file => file.Path).ToArray();
    }
}
