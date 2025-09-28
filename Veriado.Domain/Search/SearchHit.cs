namespace Veriado.Domain.Search;

/// <summary>
/// Represents a search hit returned from the full-text search index.
/// </summary>
public sealed record SearchHit(
    Guid FileId,
    string Title,
    string Mime,
    string? Snippet,
    double Score,
    DateTimeOffset LastModifiedUtc);
