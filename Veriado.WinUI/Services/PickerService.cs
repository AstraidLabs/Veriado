using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services;

public sealed class PickerService : IPickerService
{
    private readonly IWindowProvider _window;

    public PickerService(IWindowProvider window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public async Task<string?> PickFolderAsync(CancellationToken ct)
    {
        var picker = new FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _window.GetWindowHandle());
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync().AsTask(ct);
        return folder?.Path;
    }

    public async Task<string[]?> PickFilesAsync(string[]? extensions, CancellationToken ct)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _window.GetWindowHandle());
        picker.FileTypeFilter.Clear();

        if (extensions is { Length: > 0 })
        {
            foreach (var ext in extensions)
            {
                if (string.IsNullOrWhiteSpace(ext))
                {
                    continue;
                }

                var normalized = ext.StartsWith('.') ? ext : $".{ext}";
                picker.FileTypeFilter.Add(normalized);
            }
        }
        else
        {
            picker.FileTypeFilter.Add("*");
        }

        var files = await picker.PickMultipleFilesAsync().AsTask(ct);
        return files?.Select(f => f.Path).ToArray();
    }
}
