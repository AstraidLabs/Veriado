namespace Veriado.Infrastructure.Search;

/// <summary>
/// Coordinates updates across the SQLite FTS5 index and the Lucene.Net index.
/// </summary>
internal sealed class HybridSearchIndexer : ISearchIndexer
{
    private readonly SqliteFts5Indexer _ftsIndexer;
    private readonly LuceneSearchIndexer _luceneIndexer;
    private readonly InfrastructureOptions _options;
    private readonly ILogger<HybridSearchIndexer> _logger;
    private readonly bool _isEnabled;

    public HybridSearchIndexer(
        SqliteFts5Indexer ftsIndexer,
        LuceneSearchIndexer luceneIndexer,
        InfrastructureOptions options,
        ILogger<HybridSearchIndexer> logger)
    {
        _ftsIndexer = ftsIndexer ?? throw new ArgumentNullException(nameof(ftsIndexer));
        _luceneIndexer = luceneIndexer ?? throw new ArgumentNullException(nameof(luceneIndexer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var luceneEnabled = _options.EnableLuceneIntegration && _luceneIndexer.IsEnabled;
        _isEnabled = _options.IsFulltextAvailable && (!_options.EnableLuceneIntegration || luceneEnabled);

        if (!_options.IsFulltextAvailable)
        {
            _logger.LogWarning(
                "Hybrid search indexing disabled because SQLite FTS5 support is unavailable: {Reason}",
                _options.FulltextAvailabilityError ?? "Unknown reason.");
        }
        else if (_options.EnableLuceneIntegration && !luceneEnabled)
        {
            _logger.LogWarning(
                "Hybrid search indexing disabled because Lucene.Net integration could not be initialised. Check Lucene configuration settings.");
        }
    }

    public Task IndexAsync(SearchDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!_isEnabled)
        {
            return Task.CompletedTask;
        }

        Func<CancellationToken, Task>? beforeCommit = null;
        if (_options.EnableLuceneIntegration && _luceneIndexer.IsEnabled)
        {
            beforeCommit = ct => _luceneIndexer.IndexAsync(document, ct);
        }

        return _ftsIndexer.IndexAsync(document, beforeCommit, cancellationToken);
    }

    public Task DeleteAsync(Guid fileId, CancellationToken cancellationToken)
    {
        if (!_isEnabled)
        {
            return Task.CompletedTask;
        }

        Func<CancellationToken, Task>? beforeCommit = null;
        if (_options.EnableLuceneIntegration && _luceneIndexer.IsEnabled)
        {
            beforeCommit = ct => _luceneIndexer.DeleteAsync(fileId, ct);
        }

        return _ftsIndexer.DeleteAsync(fileId, beforeCommit, cancellationToken);
    }
}
