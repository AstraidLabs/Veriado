namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides thread-safe access to the SQLite FTS5 search index.
/// </summary>
internal sealed class SqliteFts5Indexer : ISearchIndexer
{
    private readonly InfrastructureOptions _options;
    private readonly SuggestionMaintenanceService? _suggestionMaintenance;

    public SqliteFts5Indexer(InfrastructureOptions options, SuggestionMaintenanceService? suggestionMaintenance = null)
    {
        _options = options;
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

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var dbTransaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var transaction = (SqliteTransaction)dbTransaction;
        var helper = new SqliteFts5Transactional();

        try
        {
            await helper.IndexAsync(document, connection, transaction, beforeCommit, cancellationToken).ConfigureAwait(false);
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

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var dbTransaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var transaction = (SqliteTransaction)dbTransaction;
        var helper = new SqliteFts5Transactional();

        try
        {
            await helper.DeleteAsync(fileId, connection, transaction, beforeCommit, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private SqliteConnection CreateConnection()
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("Infrastructure has not been initialised with a connection string.");
        }

        return new SqliteConnection(_options.ConnectionString);
    }
}
