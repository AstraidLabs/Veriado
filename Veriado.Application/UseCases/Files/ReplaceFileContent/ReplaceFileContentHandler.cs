using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Veriado.Application.Abstractions;
using Veriado.Application.Common;
using Veriado.Application.Common.Policies;
using Veriado.Application.DTO;
using Veriado.Application.Mapping;
using Veriado.Application.UseCases.Files.Common;
using Veriado.Domain.Files;

namespace Veriado.Application.UseCases.Files.ReplaceFileContent;

/// <summary>
/// Handles replacing file content while ensuring search index synchronization.
/// </summary>
public sealed class ReplaceFileContentHandler : FileWriteHandlerBase, IRequestHandler<ReplaceFileContentCommand, AppResult<FileDto>>
{
    private readonly ImportPolicy _importPolicy;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplaceFileContentHandler"/> class.
    /// </summary>
    public ReplaceFileContentHandler(
        IFileRepository repository,
        IClock clock,
        ImportPolicy importPolicy)
        : base(repository, clock)
    {
        _importPolicy = importPolicy;
    }

    /// <inheritdoc />
    public async Task<AppResult<FileDto>> Handle(ReplaceFileContentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            Guard.AgainstNull(request.Content, nameof(request.Content));
            _importPolicy.EnsureWithinLimit(request.Content.LongLength);

            var file = await Repository.GetAsync(request.FileId, cancellationToken);
            if (file is null)
            {
                return AppResult<FileDto>.NotFound($"File '{request.FileId}' was not found.");
            }

            var timestamp = CurrentTimestamp();
            file.ReplaceContent(request.Content, timestamp, _importPolicy.MaxContentLengthBytes);
            var options = new FilePersistenceOptions { ExtractContent = true };
            await PersistAsync(file, options, cancellationToken);
            return AppResult<FileDto>.Success(DomainToDto.ToFileDto(file));
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            return AppResult<FileDto>.FromException(ex);
        }
        catch (InvalidOperationException ex)
        {
            return AppResult<FileDto>.FromException(ex);
        }
        catch (Exception ex)
        {
            return AppResult<FileDto>.FromException(ex, "Failed to replace file content.");
        }
    }

}
