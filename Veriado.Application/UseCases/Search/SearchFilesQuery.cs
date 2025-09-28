namespace Veriado.Appl.UseCases.Search;

/// <summary>
/// Query to search for files in the indexed corpus.
/// </summary>
/// <param name="Text">The search text.</param>
/// <param name="Limit">Optional limit of hits.</param>
public sealed record SearchFilesQuery(string Text, int? Limit) : IRequest<IReadOnlyList<SearchHitDto>>;
