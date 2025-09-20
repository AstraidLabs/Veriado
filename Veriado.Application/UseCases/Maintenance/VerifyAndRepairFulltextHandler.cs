using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Veriado.Application.Abstractions;
using Veriado.Application.Common;
using Veriado.Domain.Files;

namespace Veriado.Application.UseCases.Maintenance;

/// <summary>
/// Ensures that the search index accurately reflects the file corpus.
/// </summary>
public sealed class VerifyAndRepairFulltextHandler : IRequestHandler<VerifyAndRepairFulltextCommand, AppResult<int>>
{
    private readonly IFileRepository _repository;
    private readonly ISearchIndexer _searchIndexer;
    private readonly ITextExtractor _textExtractor;
    private readonly IClock _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="VerifyAndRepairFulltextHandler"/> class.
    /// </summary>
    public VerifyAndRepairFulltextHandler(
        IFileRepository repository,
        ISearchIndexer searchIndexer,
        ITextExtractor textExtractor,
        IClock clock)
    {
        _repository = repository;
        _searchIndexer = searchIndexer;
        _textExtractor = textExtractor;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<AppResult<int>> Handle(VerifyAndRepairFulltextCommand request, CancellationToken cancellationToken)
    {
        var repaired = 0;

        await foreach (var file in _repository.StreamAllAsync(cancellationToken))
        {
            if (!request.Force && !NeedsRepair(file))
            {
                continue;
            }

            var text = request.ExtractContent ? await _textExtractor.ExtractTextAsync(file, cancellationToken) : null;
            var document = file.ToSearchDocument(text);
            await _searchIndexer.IndexAsync(document, cancellationToken);
            file.ConfirmIndexed(file.SearchIndex.SchemaVersion, _clock.UtcNow);
            await _repository.UpdateAsync(file, cancellationToken);
            repaired++;
        }

        return AppResult<int>.Success(repaired);
    }

    private static bool NeedsRepair(FileEntity file)
    {
        if (file.SearchIndex.IsStale)
        {
            return true;
        }

        var expectedHash = file.Content.Hash.Value;
        if (!string.Equals(file.SearchIndex.IndexedContentHash, expectedHash, StringComparison.Ordinal))
        {
            return true;
        }

        var expectedTitle = file.GetTitle() ?? file.Name.Value;
        if (!string.Equals(file.SearchIndex.IndexedTitle, expectedTitle, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
