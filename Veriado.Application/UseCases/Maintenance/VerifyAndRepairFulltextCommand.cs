namespace Veriado.Appl.UseCases.Maintenance;

/// <summary>
/// Command that verifies the search index and repairs entries when necessary.
/// </summary>
/// <param name="Force">When set, reindexes all files regardless of state.</param>
public sealed record VerifyAndRepairFulltextCommand(bool Force = false) : IRequest<AppResult<int>>;
