using System.Threading;
using System.Threading.Tasks;

namespace Veriado.WinUI.Services.Abstractions;

public interface IHotStateService
{
    string? LastQuery { get; set; }
    
    string? LastFolder { get; set; }

    int PageSize { get; set; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
}
