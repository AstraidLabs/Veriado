using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Appl.Abstractions;
using Veriado.Domain.Files;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Coordinates Lucene indexing based on the configured infrastructure mode.
/// </summary>
internal sealed class SearchIndexCoordinator : ISearchIndexCoordinator
{
    private readonly ISearchIndexer _searchIndexer;
    private readonly InfrastructureOptions _options;
    private readonly OutboxDrainService _outboxDrainService;

    public SearchIndexCoordinator(
        ISearchIndexer searchIndexer,
        InfrastructureOptions options,
        OutboxDrainService outboxDrainService)
    {
        _searchIndexer = searchIndexer ?? throw new ArgumentNullException(nameof(searchIndexer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _outboxDrainService = outboxDrainService ?? throw new ArgumentNullException(nameof(outboxDrainService));
    }

    public async Task<bool> IndexAsync(
        FileEntity file,
        FilePersistenceOptions options,
        DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (_options.SearchIndexingMode == SearchIndexingMode.Outbox && options.AllowDeferredIndexing)
        {
            return false;
        }

        var document = file.ToSearchDocument();
        await _searchIndexer.IndexAsync(document, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task SearchIndexRefreshAsync(CancellationToken cancellationToken)
    {
        if (_options.SearchIndexingMode != SearchIndexingMode.Outbox)
        {
            return;
        }

        await _outboxDrainService.DrainAsync(cancellationToken).ConfigureAwait(false);
    }
}
