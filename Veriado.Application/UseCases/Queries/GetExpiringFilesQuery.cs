using System;
using System.Collections.Generic;
using MediatR;
using Veriado.Application.DTO;

namespace Veriado.Application.UseCases.Queries;

/// <summary>
/// Query to retrieve files that are expiring within a configured lead time.
/// </summary>
/// <param name="LeadTime">Optional lead time override before expiration.</param>
public sealed record GetExpiringFilesQuery(TimeSpan? LeadTime) : IRequest<IReadOnlyList<FileListItemDto>>;
