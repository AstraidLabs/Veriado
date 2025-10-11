using System;
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
    private readonly TrigramIndexOptions _trigramOptions;
    private readonly FtsWriteAheadService _writeAhead;

    public SqliteFts5Indexer(
        InfrastructureOptions options,
        ILogger<SqliteFts5Indexer> logger,
        IAnalyzerFactory analyzerFactory,
        ISqliteConnectionFactory connectionFactory,
        TrigramIndexOptions trigramOptions,
        FtsWriteAheadService writeAhead,
        SuggestionMaintenanceService? suggestionMaintenance = null)
    {
        _options = options;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _trigramOptions = trigramOptions ?? throw new ArgumentNullException(nameof(trigramOptions));
        _writeAhead = writeAhead ?? throw new ArgumentNullException(nameof(writeAhead));
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
        await using var dbTransaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var transaction = (SqliteTransaction)dbTransaction;
        var helper = new SqliteFts5Transactional(_analyzerFactory, _trigramOptions, _writeAhead);

        try
        {
            _logger.LogInformation("Standalone FTS upsert for file {FileId}", document.FileId);
            await helper
                .IndexAsync(document, connection, transaction, beforeCommit, cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
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
        await using var dbTransaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var transaction = (SqliteTransaction)dbTransaction;
        var helper = new SqliteFts5Transactional(_analyzerFactory, _trigramOptions, _writeAhead);

        try
        {
            _logger.LogInformation("Standalone FTS delete for file {FileId}", fileId);
            await helper
                .DeleteAsync(fileId, connection, transaction, beforeCommit, cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

}
