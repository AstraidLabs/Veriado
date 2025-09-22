using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Veriado.Application.Abstractions;
using Veriado.Application.Mapping;
using Veriado.Contracts.Files;

namespace Veriado.Application.UseCases.Queries;

/// <summary>
/// Handles retrieval of detailed file projections.
/// </summary>
public sealed class GetFileDetailHandler : IRequestHandler<GetFileDetailQuery, FileDetailDto?>
{
    private readonly IFileReadRepository _readRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetFileDetailHandler"/> class.
    /// </summary>
    public GetFileDetailHandler(IFileReadRepository readRepository)
    {
        _readRepository = readRepository;
    }

    /// <inheritdoc />
    public async Task<FileDetailDto?> Handle(GetFileDetailQuery request, CancellationToken cancellationToken)
    {
        var detail = await _readRepository.GetDetailAsync(request.FileId, cancellationToken);
        return detail is null ? null : DomainToDto.ToDetailDto(detail);
    }
}
