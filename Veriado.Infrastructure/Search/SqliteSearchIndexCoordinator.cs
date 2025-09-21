using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Veriado.Application.Abstractions;
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

    public async Task<bool> IndexAsync(FileEntity file, bool extractContent, bool allowDeferred, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (_options.FtsIndexingMode == FtsIndexingMode.Outbox && allowDeferred)
        {
            _logger.LogDebug("Search indexing deferred to outbox for file {FileId}", file.Id);
            return false;
        }

        var text = extractContent
            ? await _textExtractor.ExtractTextAsync(file, cancellationToken).ConfigureAwait(false)
            : null;

        var document = file.ToSearchDocument(text);
        await _searchIndexer.IndexAsync(document, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
