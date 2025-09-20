using System.Threading;
using System.Threading.Tasks;
using Veriado.Domain.Files;

namespace Veriado.Application.Abstractions;

/// <summary>
/// Provides facilities to extract searchable text from file content.
/// </summary>
public interface ITextExtractor
{
    /// <summary>
    /// Extracts plain text from the supplied file aggregate.
    /// </summary>
    /// <param name="file">The file aggregate whose content should be analyzed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The extracted text or <see langword="null"/> when no text could be extracted.</returns>
    Task<string?> ExtractAsync(FileEntity file, CancellationToken cancellationToken);
}
