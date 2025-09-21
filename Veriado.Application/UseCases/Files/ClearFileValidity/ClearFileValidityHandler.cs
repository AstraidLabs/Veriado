using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Veriado.Application.Abstractions;
using Veriado.Application.Common;
using Veriado.Application.DTO;
using Veriado.Application.Mapping;
using Veriado.Application.UseCases.Files.Common;
using Veriado.Domain.Files;

namespace Veriado.Application.UseCases.Files.ClearFileValidity;

/// <summary>
/// Handles clearing document validity from files.
/// </summary>
public sealed class ClearFileValidityHandler : FileWriteHandlerBase, IRequestHandler<ClearFileValidityCommand, AppResult<FileDto>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ClearFileValidityHandler"/> class.
    /// </summary>
    public ClearFileValidityHandler(
        IFileRepository repository,
        IEventPublisher eventPublisher,
        ISearchIndexer searchIndexer,
        ITextExtractor textExtractor,
        IClock clock)
        : base(repository, eventPublisher, searchIndexer, textExtractor, clock)
    {
    }

    /// <inheritdoc />
    public async Task<AppResult<FileDto>> Handle(ClearFileValidityCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var file = await Repository.GetAsync(request.FileId, cancellationToken);
            if (file is null)
            {
                return AppResult<FileDto>.NotFound($"File '{request.FileId}' was not found.");
            }

            file.ClearValidity();
            await PersistAsync(file, cancellationToken);
            return AppResult<FileDto>.Success(DomainToDto.ToFileDto(file));
        }
        catch (InvalidOperationException ex)
        {
            return AppResult<FileDto>.FromException(ex);
        }
        catch (Exception ex)
        {
            return AppResult<FileDto>.FromException(ex, "Failed to clear file validity.");
        }
    }

}
