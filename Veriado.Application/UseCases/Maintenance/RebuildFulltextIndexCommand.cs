namespace Veriado.Appl.UseCases.Maintenance;

/// <summary>
/// Requests a full rebuild of the SQLite full-text index.
/// </summary>
public sealed record RebuildFulltextIndexCommand : IRequest<AppResult<int>>;
