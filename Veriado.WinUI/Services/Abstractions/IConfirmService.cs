using System.Threading;
using System.Threading.Tasks;
using Veriado.WinUI.Services;

namespace Veriado.WinUI.Services.Abstractions;

public interface IConfirmService
{
    Task<bool> TryConfirmAsync(
        string title,
        string message,
        string confirmText,
        string cancelText,
        ConfirmOptions? options = null);
}
