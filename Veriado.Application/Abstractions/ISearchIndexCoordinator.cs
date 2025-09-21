using System.Threading;
using System.Threading.Tasks;
using Veriado.Domain.Files;

namespace Veriado.Application.Abstractions;

/// <summary>
/// Coordinates search indexing operations taking the configured infrastructure mode into account.
/// </summary>
public interface ISearchIndexCoordinator
{
    /// <summary>
    /// Executes the indexing pipeline for the provided file aggregate.
    /// </summary>
    /// <param name="file">The file aggregate to index.</param>
    /// <param name="extractContent">Indicates whether text extraction should be attempted.</param>
    /// <param name="allowDeferred">When <see langword="true"/>, the coordinator may defer indexing to the outbox pipeline.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the document was indexed immediately; otherwise <see langword="false"/>.</returns>
    Task<bool> IndexAsync(FileEntity file, bool extractContent, bool allowDeferred, CancellationToken cancellationToken);
}
