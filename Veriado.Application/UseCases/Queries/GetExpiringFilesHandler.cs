using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Veriado.Application.Abstractions;
using Veriado.Application.Common.Policies;
using Veriado.Application.DTO;
using Veriado.Application.Mapping;

namespace Veriado.Application.UseCases.Queries;

/// <summary>
/// Handles retrieval of expiring files according to the reminder policy.
/// </summary>
public sealed class GetExpiringFilesHandler : IRequestHandler<GetExpiringFilesQuery, IReadOnlyList<FileListItemDto>>
{
    private readonly IFileReadRepository _readRepository;
    private readonly ValidityReminderPolicy _policy;
    private readonly IClock _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetExpiringFilesHandler"/> class.
    /// </summary>
    public GetExpiringFilesHandler(IFileReadRepository readRepository, ValidityReminderPolicy policy, IClock clock)
    {
        _readRepository = readRepository;
        _policy = policy;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FileListItemDto>> Handle(GetExpiringFilesQuery request, CancellationToken cancellationToken)
    {
        var leadTime = request.LeadTime ?? _policy.ReminderLeadTime;
        var threshold = _clock.UtcNow + leadTime;
        var items = await _readRepository.ListExpiringAsync(threshold, cancellationToken);
        return items.Select(DomainToDto.ToListItemDto).ToArray();
    }
}
