using System;
using MediatR;
using Veriado.Application.Common;
using Veriado.Contracts.Files;

namespace Veriado.Application.UseCases.Maintenance;

/// <summary>
/// Command that forces reindexing of a single file.
/// </summary>
/// <param name="FileId">The file identifier.</param>
/// <param name="ExtractContent">Indicates whether the text extractor should be invoked.</param>
public sealed record ReindexFileCommand(Guid FileId, bool ExtractContent = false) : IRequest<AppResult<FileSummaryDto>>;
