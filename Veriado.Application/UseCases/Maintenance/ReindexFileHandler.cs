using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Veriado.Application.Abstractions;
using Veriado.Application.Common;
using Veriado.Application.DTO;
using Veriado.Application.Mapping;
using Veriado.Domain.Files;
using Veriado.Domain.ValueObjects;

namespace Veriado.Application.UseCases.Maintenance;

/// <summary>
/// Handles explicit reindexing requests for a file.
/// </summary>
public sealed class ReindexFileHandler : IRequestHandler<ReindexFileCommand, AppResult<FileDto>>
{
    private readonly IFileRepository _repository;
    private readonly IClock _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReindexFileHandler"/> class.
    /// </summary>
    public ReindexFileHandler(IFileRepository repository, IClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<AppResult<FileDto>> Handle(ReindexFileCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var file = await _repository.GetAsync(request.FileId, cancellationToken);
            if (file is null)
            {
                return AppResult<FileDto>.NotFound($"File '{request.FileId}' was not found.");
            }

            var timestamp = UtcTimestamp.From(_clock.UtcNow);
            file.RequestManualReindex(timestamp);
            var options = new FilePersistenceOptions { ExtractContent = request.ExtractContent };
            await _repository.UpdateAsync(file, options, cancellationToken).ConfigureAwait(false);
            return AppResult<FileDto>.Success(DomainToDto.ToFileDto(file));
        }
        catch (Exception ex)
        {
            return AppResult<FileDto>.FromException(ex, "Failed to reindex the file.");
        }
    }
}
