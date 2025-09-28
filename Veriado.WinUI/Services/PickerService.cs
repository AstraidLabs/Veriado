using Windows.Storage.Pickers;

namespace Veriado.WinUI.Services;

public sealed class PickerService : IPickerService
{
    private readonly IWindowProvider _window;

    public PickerService(IWindowProvider window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _window.GetHwnd());
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public async Task<string[]?> PickFilesAsync(string[]? extensions = null, bool multiple = true)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _window.GetHwnd());
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

        if (multiple)
        {
            var files = await picker.PickMultipleFilesAsync();
            if (files is null)
            {
                return null;
            }

            var paths = new string[files.Count];
            for (var i = 0; i < files.Count; i++)
            {
                paths[i] = files[i].Path;
            }

            return paths;
        }

        var file = await picker.PickSingleFileAsync();
        return file is null ? null : new[] { file.Path };
    }
}
