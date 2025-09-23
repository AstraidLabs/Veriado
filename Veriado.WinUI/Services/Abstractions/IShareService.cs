using System.Threading;
using System.Threading.Tasks;

namespace Veriado.Services.Abstractions;

public interface IShareService
{
    Task ShareTextAsync(string title, string text, CancellationToken cancellationToken = default);

    Task ShareFileAsync(string title, string filePath, CancellationToken cancellationToken = default);
}
