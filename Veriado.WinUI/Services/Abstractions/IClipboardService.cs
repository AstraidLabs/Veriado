using System.Threading.Tasks;

namespace Veriado.Services.Abstractions;

public interface IClipboardService
{
    Task CopyTextAsync(string text);
}
