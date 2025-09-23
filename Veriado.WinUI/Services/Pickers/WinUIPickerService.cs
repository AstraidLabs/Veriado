using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Veriado.WinUI.Services.Pickers;

public sealed class WinUIPickerService : IPickerService
{
    private static IntPtr GetWindowHandle(Window window)
        => WindowNative.GetWindowHandle(window ?? throw new ArgumentNullException(nameof(window)));

    public async Task<string?> PickFolderAsync(Window window)
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, GetWindowHandle(window));
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public async Task<IReadOnlyList<string>> PickFilesAsync(Window window, string[]? filters = null)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, GetWindowHandle(window));

        if (filters is { Length: > 0 })
        {
            foreach (var filter in filters)
            {
                if (!string.IsNullOrWhiteSpace(filter))
                {
                    picker.FileTypeFilter.Add(filter);
                }
            }
        }
        else
        {
            picker.FileTypeFilter.Add("*");
        }

        var files = await picker.PickMultipleFilesAsync();
        return files?.Select(file => file.Path).ToArray() ?? Array.Empty<string>();
    }
}
