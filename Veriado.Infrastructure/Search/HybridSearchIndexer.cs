using System;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Appl.Abstractions;
using Veriado.Domain.Search;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Coordinates updates across the SQLite FTS5 index and the Lucene.Net index.
/// </summary>
internal sealed class HybridSearchIndexer : ISearchIndexer
{
    private readonly SqliteFts5Indexer _ftsIndexer;
    private readonly LuceneSearchIndexer _luceneIndexer;

    public HybridSearchIndexer(SqliteFts5Indexer ftsIndexer, LuceneSearchIndexer luceneIndexer)
    {
        _ftsIndexer = ftsIndexer ?? throw new ArgumentNullException(nameof(ftsIndexer));
        _luceneIndexer = luceneIndexer ?? throw new ArgumentNullException(nameof(luceneIndexer));
    }

    public async Task IndexAsync(SearchDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        await _ftsIndexer.IndexAsync(document, cancellationToken).ConfigureAwait(false);
        await _luceneIndexer.IndexAsync(document, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid fileId, CancellationToken cancellationToken)
    {
        await _ftsIndexer.DeleteAsync(fileId, cancellationToken).ConfigureAwait(false);
        await _luceneIndexer.DeleteAsync(fileId, cancellationToken).ConfigureAwait(false);
    }
}
