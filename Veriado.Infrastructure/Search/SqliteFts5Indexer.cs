using System;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Veriado.Appl.Search;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides thread-safe access to the SQLite FTS5 search index.
/// </summary>
internal sealed class SqliteFts5Indexer : ISearchIndexer
{
    private readonly InfrastructureOptions _options;
    private readonly ILogger<SqliteFts5Indexer> _logger;
    private readonly SuggestionMaintenanceService? _suggestionMaintenance;
    private readonly IAnalyzerFactory _analyzerFactory;
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly FtsWriteAheadService _writeAhead;
    private readonly ILogger<SqliteFts5Transactional> _ftsLogger;

    public SqliteFts5Indexer(
        InfrastructureOptions options,
        ILogger<SqliteFts5Indexer> logger,
        IAnalyzerFactory analyzerFactory,
        ISqliteConnectionFactory connectionFactory,
        FtsWriteAheadService writeAhead,
        ILogger<SqliteFts5Transactional> ftsLogger,
        SuggestionMaintenanceService? suggestionMaintenance = null)
    {
        _options = options;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _writeAhead = writeAhead ?? throw new ArgumentNullException(nameof(writeAhead));
        _ftsLogger = ftsLogger ?? throw new ArgumentNullException(nameof(ftsLogger));
        _suggestionMaintenance = suggestionMaintenance;
    }

    public Task IndexAsync(SearchDocument document, CancellationToken cancellationToken = default)
        => IndexInternalAsync(document, beforeCommit: null, cancellationToken);

    public Task DeleteAsync(Guid fileId, CancellationToken cancellationToken = default)
        => DeleteInternalAsync(fileId, beforeCommit: null, cancellationToken);

    internal Task IndexAsync(
        SearchDocument document,
        Func<CancellationToken, Task>? beforeCommit,
        CancellationToken cancellationToken)
        => IndexInternalAsync(document, beforeCommit, cancellationToken);

    internal Task DeleteAsync(
        Guid fileId,
        Func<CancellationToken, Task>? beforeCommit,
        CancellationToken cancellationToken)
        => DeleteInternalAsync(fileId, beforeCommit, cancellationToken);

    private async Task IndexInternalAsync(
        SearchDocument document,
        Func<CancellationToken, Task>? beforeCommit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!_options.IsFulltextAvailable)
        {
            return;
        }

        await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = lease.Connection;
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, _logger, cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction sqliteTransaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var helper = new SqliteFts5Transactional(_analyzerFactory, _writeAhead, _ftsLogger);

        try
        {
            _logger.LogInformation("Standalone FTS upsert for file {FileId}", document.FileId);
            await helper
                .IndexAsync(document, connection, sqliteTransaction, beforeCommit, cancellationToken)
                .ConfigureAwait(false);
            await sqliteTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await sqliteTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        if (_suggestionMaintenance is not null)
        {
            await _suggestionMaintenance.UpsertAsync(document, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DeleteInternalAsync(
        Guid fileId,
        Func<CancellationToken, Task>? beforeCommit,
        CancellationToken cancellationToken)
    {
        if (!_options.IsFulltextAvailable)
        {
            return;
        }

        await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = lease.Connection;
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, _logger, cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction sqliteTransaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var helper = new SqliteFts5Transactional(_analyzerFactory, _writeAhead, _ftsLogger);

        try
        {
            _logger.LogInformation("Standalone FTS delete for file {FileId}", fileId);
            await helper
                .DeleteAsync(fileId, connection, sqliteTransaction, beforeCommit, cancellationToken)
                .ConfigureAwait(false);
            await sqliteTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await sqliteTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

}
