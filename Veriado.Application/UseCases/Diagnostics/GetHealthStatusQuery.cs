using MediatR;
using Veriado.Application.Common;
using Veriado.Contracts.Diagnostics;

namespace Veriado.Application.UseCases.Diagnostics;

/// <summary>
/// Query to retrieve database health information.
/// </summary>
public sealed record GetHealthStatusQuery : IRequest<AppResult<HealthStatusDto>>;
