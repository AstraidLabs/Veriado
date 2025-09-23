using MediatR;
using Veriado.Appl.Common;
using Veriado.Contracts.Diagnostics;

namespace Veriado.Appl.UseCases.Diagnostics;

/// <summary>
/// Query to retrieve database health information.
/// </summary>
public sealed record GetHealthStatusQuery : IRequest<AppResult<HealthStatusDto>>;
