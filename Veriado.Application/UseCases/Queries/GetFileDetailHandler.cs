namespace Veriado.Appl.UseCases.Queries;

/// <summary>
/// Handles retrieval of detailed file projections.
/// </summary>
public sealed class GetFileDetailHandler : IRequestHandler<GetFileDetailQuery, FileDetailDto?>
{
    private readonly IFileReadRepository _readRepository;
    private readonly IMapper _mapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetFileDetailHandler"/> class.
    /// </summary>
    public GetFileDetailHandler(IFileReadRepository readRepository, IMapper mapper)
    {
        _readRepository = readRepository;
        _mapper = mapper;
    }

    /// <inheritdoc />
    public async Task<FileDetailDto?> Handle(GetFileDetailQuery request, CancellationToken cancellationToken)
    {
        var detail = await _readRepository.GetDetailAsync(request.FileId, cancellationToken);
        return detail is null ? null : _mapper.Map<FileDetailDto>(detail);
    }
}
