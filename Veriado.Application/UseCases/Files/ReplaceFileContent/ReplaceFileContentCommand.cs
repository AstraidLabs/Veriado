using System;
using MediatR;
using Veriado.Appl.Common;
using Veriado.Contracts.Files;

namespace Veriado.Appl.UseCases.Files.ReplaceFileContent;

/// <summary>
/// Command that replaces the binary content of an existing file.
/// </summary>
/// <param name="FileId">The identifier of the file to update.</param>
/// <param name="Content">The new binary content.</param>
public sealed record ReplaceFileContentCommand(Guid FileId, byte[] Content) : IRequest<AppResult<FileSummaryDto>>;
