using System;
using MediatR;
using Veriado.Application.Common;
using Veriado.Contracts.Files;

namespace Veriado.Application.UseCases.Files.RenameFile;

/// <summary>
/// Command for renaming a file aggregate.
/// </summary>
/// <param name="FileId">The file identifier.</param>
/// <param name="Name">The new file name without extension.</param>
public sealed record RenameFileCommand(Guid FileId, string Name) : IRequest<AppResult<FileSummaryDto>>;
