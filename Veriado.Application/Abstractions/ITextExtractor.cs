using System.Threading;
using System.Threading.Tasks;
using Veriado.Domain.Files;

namespace Veriado.Application.Abstractions;

/// <summary>
/// Extracts textual content from file aggregates for indexing purposes.
/// </summary>
public interface ITextExtractor
{
    /// <summary>
    /// Extracts text from the provided file aggregate.
    /// </summary>
    /// <param name="file">The file aggregate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The extracted text or <see langword="null"/> when none could be produced.</returns>
    Task<string?> ExtractTextAsync(FileEntity file, CancellationToken cancellationToken);
}
