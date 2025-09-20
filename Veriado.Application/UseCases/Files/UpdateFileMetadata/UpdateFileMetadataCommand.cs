using System;
using MediatR;
using Veriado.Application.Common;
using Veriado.Application.DTO;

namespace Veriado.Application.UseCases.Files.UpdateFileMetadata;

/// <summary>
/// Command to update the core metadata of a file such as MIME type and author.
/// </summary>
/// <param name="FileId">The identifier of the file.</param>
/// <param name="Mime">The optional new MIME type.</param>
/// <param name="Author">The optional new author.</param>
public sealed record UpdateFileMetadataCommand(Guid FileId, string? Mime, string? Author) : IRequest<AppResult<FileDto>>;
