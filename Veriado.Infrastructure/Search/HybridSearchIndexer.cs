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

    public Task IndexAsync(SearchDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        return _ftsIndexer.IndexAsync(
            document,
            beforeCommit: ct => _luceneIndexer.IndexAsync(document, ct),
            cancellationToken);
    }

    public Task DeleteAsync(Guid fileId, CancellationToken cancellationToken)
    {
        return _ftsIndexer.DeleteAsync(
            fileId,
            beforeCommit: ct => _luceneIndexer.DeleteAsync(fileId, ct),
            cancellationToken);
    }
}
