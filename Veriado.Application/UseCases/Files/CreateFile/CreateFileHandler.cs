using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Veriado.Application.Abstractions;
using Veriado.Application.Common;
using Veriado.Application.Common.Policies;
using Veriado.Application.UseCases.Files.Common;
using Veriado.Domain.Files;
using Veriado.Domain.ValueObjects;

namespace Veriado.Application.UseCases.Files.CreateFile;

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
        IEventPublisher eventPublisher,
        ISearchIndexer searchIndexer,
        ITextExtractor textExtractor,
        ImportPolicy importPolicy,
        IClock clock)
        : base(repository, eventPublisher, searchIndexer, textExtractor, clock)
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
            var file = FileEntity.CreateNew(name, extension, mime, request.Author, request.Content, _importPolicy.MaxContentLengthBytes);

            await PersistNewAsync(file, cancellationToken);
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
