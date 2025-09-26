using System;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using MediatR;
using Veriado.Appl.Abstractions;
using Veriado.Appl.Common;
using Veriado.Contracts.Files;
using Veriado.Domain.Files;
using Veriado.Domain.ValueObjects;

namespace Veriado.Appl.UseCases.Maintenance;

/// <summary>
/// Handles explicit reindexing requests for a file.
/// </summary>
public sealed class ReindexFileHandler : IRequestHandler<ReindexFileCommand, AppResult<FileSummaryDto>>
{
    private readonly IFileRepository _repository;
    private readonly IClock _clock;
    private readonly IMapper _mapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReindexFileHandler"/> class.
    /// </summary>
    public ReindexFileHandler(IFileRepository repository, IClock clock, IMapper mapper)
    {
        _repository = repository;
        _clock = clock;
        _mapper = mapper;
    }

    /// <inheritdoc />
    public async Task<AppResult<FileSummaryDto>> Handle(ReindexFileCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var file = await _repository.GetAsync(request.FileId, cancellationToken);
            if (file is null)
            {
                return AppResult<FileSummaryDto>.NotFound($"File '{request.FileId}' was not found.");
            }

            var timestamp = UtcTimestamp.From(_clock.UtcNow);
            file.RequestManualReindex(timestamp);
            await _repository.UpdateAsync(file, FilePersistenceOptions.Default, cancellationToken).ConfigureAwait(false);
            return AppResult<FileSummaryDto>.Success(_mapper.Map<FileSummaryDto>(file));
        }
        catch (Exception ex)
        {
            return AppResult<FileSummaryDto>.FromException(ex, "Failed to reindex the file.");
        }
    }
}
