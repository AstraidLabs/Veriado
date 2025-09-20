using System;
using System.Collections.Generic;
using MediatR;
using Veriado.Application.Common;

namespace Veriado.Application.UseCases.Maintenance;

/// <summary>
/// Command to reindex multiple files in bulk.
/// </summary>
/// <param name="FileIds">The collection of file identifiers.</param>
/// <param name="ExtractContent">Indicates whether binary content should be reprocessed.</param>
public sealed record BulkReindexCommand(IReadOnlyCollection<Guid> FileIds, bool ExtractContent = false) : IRequest<AppResult<int>>;
