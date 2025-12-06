using System.Threading;
using System.Threading.Tasks;

namespace Veriado.Appl.Abstractions;

public interface IStorageRootSettingsService
{
    Task<string> GetCurrentRootAsync(CancellationToken cancellationToken);
    Task<string> GetEffectiveRootAsync(CancellationToken cancellationToken);
    Task ChangeRootAsync(string newRoot, CancellationToken cancellationToken);
}
