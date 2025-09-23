using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace Veriado.WinUI.Services.Pickers;

public interface IPickerService
{
    Task<string?> PickFolderAsync(Window window);

    Task<IReadOnlyList<string>> PickFilesAsync(Window window, string[]? filters = null);
}
