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

    /// <summary>
    /// Initializes a new instance of the <see cref="VerifyAndRepairFulltextHandler"/> class.
    /// </summary>
    public VerifyAndRepairFulltextHandler(IFileRepository repository)
    {
        _repository = repository;
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

            file.RequestManualReindex();
            await _repository.UpdateAsync(file, cancellationToken).ConfigureAwait(false);
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
