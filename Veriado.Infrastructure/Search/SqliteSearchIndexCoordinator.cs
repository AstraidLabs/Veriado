using System;
using Microsoft.Data.Sqlite;
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
    private readonly ITrigramQueryBuilder _trigramBuilder;

    public SqliteSearchIndexCoordinator(
        InfrastructureOptions options,
        ILogger<SqliteSearchIndexCoordinator> logger,
        IAnalyzerFactory analyzerFactory,
        TrigramIndexOptions trigramOptions,
        FtsWriteAheadService writeAhead,
        ITrigramQueryBuilder trigramBuilder)
    {
        _options = options;
        _logger = logger;
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
        _trigramOptions = trigramOptions ?? throw new ArgumentNullException(nameof(trigramOptions));
        _writeAhead = writeAhead ?? throw new ArgumentNullException(nameof(writeAhead));
        _trigramBuilder = trigramBuilder ?? throw new ArgumentNullException(nameof(trigramBuilder));
    }

    public async Task<bool> IndexAsync(FileEntity file, FilePersistenceOptions options, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(transaction);

        if (!_options.IsFulltextAvailable)
        {
            _logger.LogDebug("Skipping full-text indexing for file {FileId} because FTS5 support is unavailable.", file.Id);
            return false;
        }

        var sqliteConnection = transaction.Connection as SqliteConnection
            ?? throw new InvalidOperationException("SQLite connection is unavailable for the active transaction.");

        var document = file.ToSearchDocument();
        var helper = new SqliteFts5Transactional(_analyzerFactory, _trigramOptions, _writeAhead, _trigramBuilder);
        _logger.LogInformation(
            "Coordinating FTS upsert for file {FileId} within ambient transaction",
            file.Id);
        await helper.IndexAsync(document, sqliteConnection, transaction, beforeCommit: null, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

}
