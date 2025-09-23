using System.Threading;
using System.Threading.Tasks;

namespace Veriado.WinUI.Services.Abstractions;

public interface IPickerService
{
    Task<string?> PickFolderAsync(CancellationToken ct);
    Task<string[]?> PickFilesAsync(string[]? extensions, CancellationToken ct);
}
