using System.Threading.Tasks;

namespace Veriado.WinUI.Services.Abstractions;

public interface IClipboardService
{
    Task CopyTextAsync(string text);
}
