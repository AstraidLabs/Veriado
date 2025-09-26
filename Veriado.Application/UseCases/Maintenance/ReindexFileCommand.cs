using System;
using MediatR;
using Veriado.Appl.Common;
using Veriado.Contracts.Files;

namespace Veriado.Appl.UseCases.Maintenance;

/// <summary>
/// Command that forces reindexing of a single file.
/// </summary>
/// <param name="FileId">The file identifier.</param>
public sealed record ReindexFileCommand(Guid FileId) : IRequest<AppResult<FileSummaryDto>>;
