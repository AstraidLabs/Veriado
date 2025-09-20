using MediatR;
using Veriado.Application.Common;

namespace Veriado.Application.UseCases.Maintenance;

/// <summary>
/// Command that verifies the search index and repairs entries when necessary.
/// </summary>
/// <param name="ExtractContent">Indicates whether to re-extract text during repair.</param>
/// <param name="Force">When set, reindexes all files regardless of state.</param>
public sealed record VerifyAndRepairFulltextCommand(bool ExtractContent = false, bool Force = false) : IRequest<AppResult<int>>;
