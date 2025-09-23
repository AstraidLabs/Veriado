using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using MediatR;
using Veriado.Appl.Abstractions;
using Veriado.Appl.Common.Policies;
using Veriado.Contracts.Files;

namespace Veriado.Appl.UseCases.Queries;

/// <summary>
/// Handles retrieval of expiring files according to the reminder policy.
/// </summary>
public sealed class GetExpiringFilesHandler : IRequestHandler<GetExpiringFilesQuery, IReadOnlyList<FileListItemDto>>
{
    private readonly IFileReadRepository _readRepository;
    private readonly ValidityReminderPolicy _policy;
    private readonly IClock _clock;
    private readonly IMapper _mapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetExpiringFilesHandler"/> class.
    /// </summary>
    public GetExpiringFilesHandler(IFileReadRepository readRepository, ValidityReminderPolicy policy, IClock clock, IMapper mapper)
    {
        _readRepository = readRepository;
        _policy = policy;
        _clock = clock;
        _mapper = mapper;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FileListItemDto>> Handle(GetExpiringFilesQuery request, CancellationToken cancellationToken)
    {
        var leadTime = request.LeadTime ?? _policy.ReminderLeadTime;
        var threshold = _clock.UtcNow + leadTime;
        var items = await _readRepository.ListExpiringAsync(threshold, cancellationToken);
        return _mapper.Map<IReadOnlyList<FileListItemDto>>(items);
    }
}
