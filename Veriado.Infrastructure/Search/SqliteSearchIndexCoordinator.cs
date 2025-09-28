using System.Data.Common;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Coordinates immediate or deferred indexing operations based on infrastructure configuration.
/// </summary>
internal sealed class SqliteSearchIndexCoordinator : ISearchIndexCoordinator
{
    private readonly ISearchIndexer _searchIndexer;
    private readonly InfrastructureOptions _options;
    private readonly ILogger<SqliteSearchIndexCoordinator> _logger;

    public SqliteSearchIndexCoordinator(
        ISearchIndexer searchIndexer,
        InfrastructureOptions options,
        ILogger<SqliteSearchIndexCoordinator> logger)
    {
        _searchIndexer = searchIndexer;
        _options = options;
        _logger = logger;
    }

    public async Task<bool> IndexAsync(FileEntity file, FilePersistenceOptions options, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (!_options.IsFulltextAvailable)
        {
            _logger.LogDebug("Skipping full-text indexing for file {FileId} because FTS5 support is unavailable.", file.Id);
            return false;
        }

        if (_options.FtsIndexingMode == FtsIndexingMode.Outbox && options.AllowDeferredIndexing)
        {
            _logger.LogDebug("Search indexing deferred to outbox for file {FileId}", file.Id);
            return false;
        }

        if (transaction is not null && transaction is not SqliteTransaction)
        {
            throw new InvalidOperationException("SQLite transaction is required for full-text indexing operations.");
        }

        var sqliteTransaction = (SqliteTransaction?)transaction;

        var document = file.ToSearchDocument();
        if (sqliteTransaction is not null)
        {
            var sqliteConnection = (SqliteConnection)sqliteTransaction.Connection!;
            var helper = new SqliteFts5Transactional();
            await helper.IndexAsync(document, sqliteConnection, sqliteTransaction, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (_options.FtsIndexingMode == FtsIndexingMode.SameTransaction)
        {
            throw new InvalidOperationException("SQLite transaction is required for same-transaction indexing.");
        }

        await _searchIndexer.IndexAsync(document, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
