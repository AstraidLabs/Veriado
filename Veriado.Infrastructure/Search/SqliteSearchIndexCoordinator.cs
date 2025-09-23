using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Veriado.Appl.Abstractions;
using Veriado.Domain.Files;
using Veriado.Infrastructure.Persistence.Options;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Coordinates immediate or deferred indexing operations based on infrastructure configuration.
/// </summary>
internal sealed class SqliteSearchIndexCoordinator : ISearchIndexCoordinator
{
    private readonly ITextExtractor _textExtractor;
    private readonly ISearchIndexer _searchIndexer;
    private readonly InfrastructureOptions _options;
    private readonly ILogger<SqliteSearchIndexCoordinator> _logger;

    public SqliteSearchIndexCoordinator(
        ITextExtractor textExtractor,
        ISearchIndexer searchIndexer,
        InfrastructureOptions options,
        ILogger<SqliteSearchIndexCoordinator> logger)
    {
        _textExtractor = textExtractor;
        _searchIndexer = searchIndexer;
        _options = options;
        _logger = logger;
    }

    public async Task<bool> IndexAsync(FileEntity file, FilePersistenceOptions options, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

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

        var text = options.ExtractContent
            ? await _textExtractor.ExtractTextAsync(file, cancellationToken).ConfigureAwait(false)
            : null;

        var document = file.ToSearchDocument(text);
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
