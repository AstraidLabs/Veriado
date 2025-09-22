using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.WinUI.Collections;
using Veriado.Application.UseCases.Queries.FileGrid;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;
using Veriado.Services.Files;

namespace Veriado.WinUI.Collections;

/// <summary>
/// Incremental source that delegates server-side paging to <see cref="IFileQueryService"/>.
/// </summary>
public sealed class FilesIncrementalSource : IIncrementalSource<FileSummaryDto>
{
    private readonly IFileQueryService _fileQueryService;
    private readonly Func<FileGridQueryDto> _queryFactory;
    private readonly Action<PageResult<FileSummaryDto>> _pageCallback;

    public FilesIncrementalSource(
        IFileQueryService fileQueryService,
        Func<FileGridQueryDto> queryFactory,
        Action<PageResult<FileSummaryDto>> pageCallback)
    {
        _fileQueryService = fileQueryService ?? throw new ArgumentNullException(nameof(fileQueryService));
        _queryFactory = queryFactory ?? throw new ArgumentNullException(nameof(queryFactory));
        _pageCallback = pageCallback ?? throw new ArgumentNullException(nameof(pageCallback));
    }

    /// <inheritdoc />
    public async Task<IEnumerable<FileSummaryDto>> GetPagedItemsAsync(int pageIndex, int pageSize, CancellationToken cancellationToken)
    {
        var dto = _queryFactory();
        var request = dto with
        {
            Page = dto.Page with
            {
                Page = pageIndex + 1,
                PageSize = pageSize,
            },
        };

        var result = await _fileQueryService
            .GetGridAsync(new FileGridQuery(request), cancellationToken)
            .ConfigureAwait(false);

        _pageCallback(result);
        return result.Items;
    }
}
