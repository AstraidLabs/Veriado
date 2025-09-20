using System;
using MediatR;
using Veriado.Application.Common;
using Veriado.Application.DTO;

namespace Veriado.Application.UseCases.Files.SetFileReadOnly;

/// <summary>
/// Command for toggling the read-only status of a file.
/// </summary>
/// <param name="FileId">The file identifier.</param>
/// <param name="IsReadOnly">The desired read-only status.</param>
public sealed record SetFileReadOnlyCommand(Guid FileId, bool IsReadOnly) : IRequest<AppResult<FileDto>>;
