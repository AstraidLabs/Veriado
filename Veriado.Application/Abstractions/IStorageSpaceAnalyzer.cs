using System.Threading;
using System.Threading.Tasks;

namespace Veriado.Appl.Abstractions;

public interface IStorageSpaceAnalyzer
{
    Task<long> GetAvailableBytesAsync(string path, CancellationToken cancellationToken);

    Task<long> CalculateDirectorySizeAsync(string path, CancellationToken cancellationToken);

    Task<long> GetFileSizeAsync(string path, CancellationToken cancellationToken);
}
