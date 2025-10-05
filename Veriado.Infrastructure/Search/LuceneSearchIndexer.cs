using Veriado.Appl.Abstractions;
using Veriado.Domain.Search;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides Lucene-based indexing operations for search documents.
/// </summary>
internal sealed class LuceneSearchIndexer : ISearchIndexer
{
    private readonly LuceneIndexManager _luceneIndex;
    private readonly SuggestionMaintenanceService? _suggestionMaintenance;

    public LuceneSearchIndexer(
        LuceneIndexManager luceneIndex,
        SuggestionMaintenanceService? suggestionMaintenance = null)
    {
        _luceneIndex = luceneIndex ?? throw new ArgumentNullException(nameof(luceneIndex));
        _suggestionMaintenance = suggestionMaintenance;
    }

    public async Task IndexAsync(SearchDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        await _luceneIndex.IndexAsync(document, cancellationToken).ConfigureAwait(false);

        if (_suggestionMaintenance is not null)
        {
            await _suggestionMaintenance.UpsertAsync(document, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task DeleteAsync(Guid fileId, CancellationToken cancellationToken)
    {
        return _luceneIndex.DeleteAsync(fileId, cancellationToken);
    }
}
