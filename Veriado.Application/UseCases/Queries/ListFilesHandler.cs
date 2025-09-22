using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Veriado.Application.Abstractions;
using Veriado.Application.Common;
using Veriado.Application.Mapping;
using Veriado.Contracts.Files;

namespace Veriado.Application.UseCases.Queries;

/// <summary>
/// Handles listing files with paging support.
/// </summary>
public sealed class ListFilesHandler : IRequestHandler<ListFilesQuery, Page<FileListItemDto>>
{
    private readonly IFileReadRepository _readRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListFilesHandler"/> class.
    /// </summary>
    public ListFilesHandler(IFileReadRepository readRepository)
    {
        _readRepository = readRepository;
    }

    /// <inheritdoc />
    public async Task<Page<FileListItemDto>> Handle(ListFilesQuery request, CancellationToken cancellationToken)
    {
        var pageRequest = new PageRequest(request.PageNumber, request.PageSize);
        var result = await _readRepository.ListAsync(pageRequest, cancellationToken);
        var items = result.Items.Select(DomainToDto.ToFileListItemDto).ToList();
        return new Page<FileListItemDto>(items, result.PageNumber, result.PageSize, result.TotalCount);
    }
}
