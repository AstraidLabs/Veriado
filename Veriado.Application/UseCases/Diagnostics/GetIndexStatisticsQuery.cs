using MediatR;
using Veriado.Application.Common;
using Veriado.Contracts.Diagnostics;

namespace Veriado.Application.UseCases.Diagnostics;

/// <summary>
/// Query to retrieve high-level search index metrics.
/// </summary>
public sealed record GetIndexStatisticsQuery : IRequest<AppResult<IndexStatisticsDto>>;
