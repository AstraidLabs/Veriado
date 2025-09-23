using System.Threading.Tasks;

namespace Veriado.WinUI.Services.Pickers;

public interface IPickerService
{
    Task<string?> PickFolderAsync();

    Task<string[]?> PickFilesAsync(string[]? extensions = null);
}
