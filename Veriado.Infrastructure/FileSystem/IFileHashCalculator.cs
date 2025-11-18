using System.Threading;
using System.Threading.Tasks;
using Veriado.Domain.ValueObjects;

namespace Veriado.Infrastructure.FileSystem;

/// <summary>
/// Computes hashes for files stored on disk.
/// </summary>
public interface IFileHashCalculator
{
    /// <summary>
    /// Computes the SHA-256 hash for a file at the specified path.
    /// </summary>
    /// <param name="filePath">The full path of the file to hash.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The computed <see cref="FileHash"/>.</returns>
    Task<FileHash> ComputeSha256Async(string filePath, CancellationToken cancellationToken);
}
