namespace Veriado.Appl.UseCases.Maintenance;

/// <summary>
/// Command to reindex multiple files in bulk.
/// </summary>
/// <param name="FileIds">The collection of file identifiers.</param>
public sealed record BulkReindexCommand(IReadOnlyCollection<Guid> FileIds) : IRequest<AppResult<int>>;
