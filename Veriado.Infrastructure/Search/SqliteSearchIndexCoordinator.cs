using System;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Veriado.Appl.Search;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Coordinates full-text indexing operations executed inside the ambient SQLite transaction.
/// </summary>
internal sealed class SqliteSearchIndexCoordinator : ISearchIndexCoordinator
{
    private const string IndexingMode = "SameTransaction";

    private readonly InfrastructureOptions _options;
    private readonly ILogger<SqliteSearchIndexCoordinator> _logger;
    private readonly IAnalyzerFactory _analyzerFactory;
    private readonly TrigramIndexOptions _trigramOptions;
    private readonly FtsWriteAheadService _writeAhead;

    public SqliteSearchIndexCoordinator(
        InfrastructureOptions options,
        ILogger<SqliteSearchIndexCoordinator> logger,
        IAnalyzerFactory analyzerFactory,
        TrigramIndexOptions trigramOptions,
        FtsWriteAheadService writeAhead)
    {
        _options = options;
        _logger = logger;
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
        _trigramOptions = trigramOptions ?? throw new ArgumentNullException(nameof(trigramOptions));
        _writeAhead = writeAhead ?? throw new ArgumentNullException(nameof(writeAhead));
    }

    public async Task<bool> IndexAsync(FileEntity file, FilePersistenceOptions options, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (!_options.IsFulltextAvailable)
        {
            _logger.LogDebug("Skipping full-text indexing for file {FileId} because FTS5 support is unavailable.", file.Id);
            return false;
        }

        if (options.AllowDeferredIndexing)
        {
            _logger.LogWarning(
                "Deferred indexing requested for file {FileId}, but FtsIndexingMode '{IndexingMode}' enforces synchronous processing.",
                file.Id,
                IndexingMode);
        }

        if (transaction is not SqliteTransaction sqliteTransaction)
        {
            throw new InvalidOperationException("SQLite transaction is required for full-text indexing operations.");
        }

        var sqliteConnection = sqliteTransaction.Connection as SqliteConnection
            ?? throw new InvalidOperationException("SQLite connection is unavailable for the active transaction.");

        var document = file.ToSearchDocument();
        var helper = new SqliteFts5Transactional(_analyzerFactory, _trigramOptions, _writeAhead);
        _logger.LogInformation(
            "Coordinating FTS upsert for file {FileId} within ambient transaction",
            file.Id);
        await helper.IndexAsync(document, sqliteConnection, sqliteTransaction, beforeCommit: null, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    #region TODO(SQLiteOnly): Remove no-op deferred indexing hook when outbox is deleted
    public Task SearchIndexRefreshAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("SearchIndexRefreshAsync invoked with no deferred indexing to process.");
        return Task.CompletedTask;
    }
    #endregion
}
