using System;
using System.Data.Common;
using Veriado.Appl.Search;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Coordinates immediate or deferred indexing operations based on infrastructure configuration.
/// </summary>
internal sealed class SqliteSearchIndexCoordinator : ISearchIndexCoordinator
{
    private readonly ISearchIndexer _searchIndexer;
    private readonly InfrastructureOptions _options;
    private readonly ILogger<SqliteSearchIndexCoordinator> _logger;
    private readonly OutboxDrainService _outboxDrainService;
    private readonly IAnalyzerFactory _analyzerFactory;
    private readonly TrigramIndexOptions _trigramOptions;
    private readonly FtsWriteAheadService _writeAhead;
    private readonly LuceneIndexManager _luceneIndex;

    public SqliteSearchIndexCoordinator(
        ISearchIndexer searchIndexer,
        InfrastructureOptions options,
        ILogger<SqliteSearchIndexCoordinator> logger,
        OutboxDrainService outboxDrainService,
        IAnalyzerFactory analyzerFactory,
        TrigramIndexOptions trigramOptions,
        FtsWriteAheadService writeAhead,
        LuceneIndexManager luceneIndex)
    {
        _searchIndexer = searchIndexer;
        _options = options;
        _logger = logger;
        _outboxDrainService = outboxDrainService;
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
        _trigramOptions = trigramOptions ?? throw new ArgumentNullException(nameof(trigramOptions));
        _writeAhead = writeAhead ?? throw new ArgumentNullException(nameof(writeAhead));
        _luceneIndex = luceneIndex ?? throw new ArgumentNullException(nameof(luceneIndex));
    }

    public async Task<bool> IndexAsync(FileEntity file, FilePersistenceOptions options, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (_options.FtsIndexingMode == FtsIndexingMode.Outbox && options.AllowDeferredIndexing)
        {
            _logger.LogDebug("Search indexing deferred to outbox for file {FileId}", file.Id);
            return false;
        }

        var document = file.ToSearchDocument();
        if (!_options.IsFulltextAvailable)
        {
            if (transaction is not null)
            {
                await _luceneIndex.IndexAsync(document, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _searchIndexer.IndexAsync(document, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }

        if (transaction is not null && transaction is not SqliteTransaction)
        {
            throw new InvalidOperationException("SQLite transaction is required for full-text indexing operations.");
        }

        var sqliteTransaction = (SqliteTransaction?)transaction;

        if (sqliteTransaction is not null)
        {
            var sqliteConnection = (SqliteConnection)sqliteTransaction.Connection!;
            var helper = new SqliteFts5Transactional(_analyzerFactory, _trigramOptions, _writeAhead);
            await helper.IndexAsync(
                    document,
                    sqliteConnection,
                    sqliteTransaction,
                    beforeCommit: ct => _luceneIndex.IndexAsync(document, ct),
                    cancellationToken)
                .ConfigureAwait(false);
            return true;
        }

        if (_options.FtsIndexingMode == FtsIndexingMode.SameTransaction)
        {
            throw new InvalidOperationException("SQLite transaction is required for same-transaction indexing.");
        }

        await _searchIndexer.IndexAsync(document, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task SearchIndexRefreshAsync(CancellationToken cancellationToken)
    {
        if (_options.FtsIndexingMode != FtsIndexingMode.Outbox)
        {
            return;
        }

        await _outboxDrainService.DrainAsync(cancellationToken).ConfigureAwait(false);
    }
}
