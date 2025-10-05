using System;
using Veriado.Appl.Search;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides thread-safe access to the SQLite FTS5 search index.
/// </summary>
internal sealed class SqliteFts5Indexer : ISearchIndexer
{
    private readonly InfrastructureOptions _options;
    private readonly SuggestionMaintenanceService? _suggestionMaintenance;
    private readonly IAnalyzerFactory _analyzerFactory;
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly TrigramIndexOptions _trigramOptions;
    private readonly FtsWriteAheadService _writeAhead;
    private readonly LuceneIndexManager _luceneIndex;

    public SqliteFts5Indexer(
        InfrastructureOptions options,
        IAnalyzerFactory analyzerFactory,
        ISqliteConnectionFactory connectionFactory,
        TrigramIndexOptions trigramOptions,
        FtsWriteAheadService writeAhead,
        LuceneIndexManager luceneIndex,
        SuggestionMaintenanceService? suggestionMaintenance = null)
    {
        _options = options;
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _trigramOptions = trigramOptions ?? throw new ArgumentNullException(nameof(trigramOptions));
        _writeAhead = writeAhead ?? throw new ArgumentNullException(nameof(writeAhead));
        _luceneIndex = luceneIndex ?? throw new ArgumentNullException(nameof(luceneIndex));
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
            if (beforeCommit is not null)
            {
                await beforeCommit(cancellationToken).ConfigureAwait(false);
            }

            await _luceneIndex.IndexAsync(document, cancellationToken).ConfigureAwait(false);

            if (_suggestionMaintenance is not null)
            {
                await _suggestionMaintenance.UpsertAsync(document, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = lease.Connection;
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var dbTransaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var transaction = (SqliteTransaction)dbTransaction;
        var helper = new SqliteFts5Transactional(_analyzerFactory, _trigramOptions, _writeAhead);

        try
        {
            var journalId = await helper
                .IndexAsync(document, connection, transaction, beforeCommit, cancellationToken, enlistJournal: false)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            if (journalId.HasValue)
            {
                await _writeAhead.ClearAsync(connection, transaction: null, journalId.Value, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        if (beforeCommit is not null)
        {
            await beforeCommit(cancellationToken).ConfigureAwait(false);
        }

        await _luceneIndex.IndexAsync(document, cancellationToken).ConfigureAwait(false);

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
            if (beforeCommit is not null)
            {
                await beforeCommit(cancellationToken).ConfigureAwait(false);
            }

            await _luceneIndex.DeleteAsync(fileId, cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = lease.Connection;
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var dbTransaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var transaction = (SqliteTransaction)dbTransaction;
        var helper = new SqliteFts5Transactional(_analyzerFactory, _trigramOptions, _writeAhead);

        try
        {
            var journalId = await helper
                .DeleteAsync(fileId, connection, transaction, beforeCommit, cancellationToken, enlistJournal: false)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            if (journalId.HasValue)
            {
                await _writeAhead.ClearAsync(connection, transaction: null, journalId.Value, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        if (beforeCommit is not null)
        {
            await beforeCommit(cancellationToken).ConfigureAwait(false);
        }

        await _luceneIndex.DeleteAsync(fileId, cancellationToken).ConfigureAwait(false);
    }

}
