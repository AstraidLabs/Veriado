namespace Veriado.Appl.UseCases.Diagnostics;

/// <summary>
/// Query to retrieve high-level search index metrics.
/// </summary>
public sealed record GetIndexStatisticsQuery : IRequest<AppResult<IndexStatisticsDto>>;
