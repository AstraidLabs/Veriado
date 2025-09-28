namespace Veriado.Appl.UseCases.Queries;

/// <summary>
/// Handles listing files with paging support.
/// </summary>
public sealed class ListFilesHandler : IRequestHandler<ListFilesQuery, Page<FileListItemDto>>
{
    private readonly IFileReadRepository _readRepository;
    private readonly IMapper _mapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListFilesHandler"/> class.
    /// </summary>
    public ListFilesHandler(IFileReadRepository readRepository, IMapper mapper)
    {
        _readRepository = readRepository;
        _mapper = mapper;
    }

    /// <inheritdoc />
    public async Task<Page<FileListItemDto>> Handle(ListFilesQuery request, CancellationToken cancellationToken)
    {
        var pageRequest = new PageRequest(request.PageNumber, request.PageSize);
        var result = await _readRepository.ListAsync(pageRequest, cancellationToken);
        var items = _mapper.Map<IReadOnlyList<FileListItemDto>>(result.Items);
        return new Page<FileListItemDto>(items, result.PageNumber, result.PageSize, result.TotalCount);
    }
}
