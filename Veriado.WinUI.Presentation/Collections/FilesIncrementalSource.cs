using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using CommunityToolkit.WinUI.Collections;
using Veriado.Contracts.Files;
using Veriado.Presentation.Models.Common;
using Veriado.Presentation.Models.Files;
using Veriado.Services.Files;

namespace Veriado.Presentation.Collections;

/// <summary>
/// Incremental source that delegates server-side paging to <see cref="IFileQueryService"/>.
/// </summary>
public sealed class FilesIncrementalSource : IIncrementalSource<FileSummaryModel>
{
    private readonly IFileQueryService _fileQueryService;
    private readonly IMapper _mapper;
    private readonly Func<FileGridQueryModel> _queryFactory;
    private readonly Action<PageResultModel<FileSummaryModel>> _pageCallback;

    public FilesIncrementalSource(
        IFileQueryService fileQueryService,
        IMapper mapper,
        Func<FileGridQueryModel> queryFactory,
        Action<PageResultModel<FileSummaryModel>> pageCallback)
    {
        _fileQueryService = fileQueryService ?? throw new ArgumentNullException(nameof(fileQueryService));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _queryFactory = queryFactory ?? throw new ArgumentNullException(nameof(queryFactory));
        _pageCallback = pageCallback ?? throw new ArgumentNullException(nameof(pageCallback));
    }

    /// <inheritdoc />
    public async Task<IEnumerable<FileSummaryModel>> GetPagedItemsAsync(int pageIndex, int pageSize, CancellationToken cancellationToken)
    {
        var model = _queryFactory();
        var dto = _mapper.Map<FileGridQueryDto>(model);
        dto = dto with
        {
            Page = dto.Page with
            {
                Page = pageIndex + 1,
                PageSize = pageSize,
            },
        };

        var result = await _fileQueryService
            .GetGridAsync(dto, cancellationToken)
            .ConfigureAwait(false);

        var mapped = _mapper.Map<PageResultModel<FileSummaryModel>>(result);
        _pageCallback(mapped);
        return mapped.Items;
    }
}
