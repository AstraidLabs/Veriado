using System;
using MediatR;
using Veriado.Application.Common;
using Veriado.Application.DTO;

namespace Veriado.Application.UseCases.Files.ClearFileValidity;

/// <summary>
/// Command to clear validity information from a file.
/// </summary>
/// <param name="FileId">The file identifier.</param>
public sealed record ClearFileValidityCommand(Guid FileId) : IRequest<AppResult<FileDto>>;
