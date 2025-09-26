using System;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using MediatR;
using Veriado.Appl.Abstractions;
using Veriado.Appl.Common;
using Veriado.Appl.Common.Policies;
using Veriado.Appl.UseCases.Files.Common;
using Veriado.Domain.Files;
using Veriado.Domain.ValueObjects;

namespace Veriado.Appl.UseCases.Files.CreateFile;

/// <summary>
/// Handles creation of new file aggregates.
/// </summary>
public sealed class CreateFileHandler : FileWriteHandlerBase, IRequestHandler<CreateFileCommand, AppResult<Guid>>
{
    private readonly ImportPolicy _importPolicy;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateFileHandler"/> class.
    /// </summary>
    public CreateFileHandler(
        IFileRepository repository,
        IClock clock,
        ImportPolicy importPolicy,
        IMapper mapper)
        : base(repository, clock, mapper)
    {
        _importPolicy = importPolicy;
    }

    /// <inheritdoc />
    public async Task<AppResult<Guid>> Handle(CreateFileCommand request, CancellationToken cancellationToken)
    {
        try
        {
            Guard.AgainstNull(request.Content, nameof(request.Content));
            _importPolicy.EnsureWithinLimit(request.Content.LongLength);

            var name = FileName.From(request.Name);
            var extension = FileExtension.From(request.Extension);
            var mime = MimeType.From(request.Mime);
            var createdAt = CurrentTimestamp();
            var file = FileEntity.CreateNew(name, extension, mime, request.Author, request.Content, createdAt, _importPolicy.MaxContentLengthBytes);

            if (await Repository.ExistsByHashAsync(file.Content.Hash, cancellationToken).ConfigureAwait(false))
            {
                return AppResult<Guid>.Conflict("A file with identical content already exists.");
            }

            var options = new FilePersistenceOptions { ExtractContent = true };
            await PersistNewAsync(file, options, cancellationToken);
            return AppResult<Guid>.Success(file.Id);
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            return AppResult<Guid>.FromException(ex);
        }
        catch (InvalidOperationException ex)
        {
            return AppResult<Guid>.FromException(ex);
        }
        catch (Exception ex)
        {
            return AppResult<Guid>.FromException(ex, "Failed to create the file.");
        }
    }

}
