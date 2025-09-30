namespace Veriado.Domain.Search;

/// <summary>
/// Represents the document projected to the search index.
/// </summary>
public sealed record SearchDocument(
    Guid FileId,
    string Title,
    string Mime,
    string? Author,
    string FileName,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ModifiedUtc);
