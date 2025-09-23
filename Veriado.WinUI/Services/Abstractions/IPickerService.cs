namespace Veriado.Services.Abstractions;

public interface IPickerService
{
    System.Threading.Tasks.Task<string?> PickFolderAsync();

    System.Threading.Tasks.Task<string[]?> PickFilesAsync(string[]? extensions = null, bool multiple = true);
}
