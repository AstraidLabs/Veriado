using System;

namespace Veriado.Contracts.Search;

/// <summary>
/// Represents a single hit returned by the search subsystem.
/// </summary>
/// <param name="FileId">The identifier of the matching file.</param>
/// <param name="Title">The display title of the hit.</param>
/// <param name="Mime">The MIME type of the file.</param>
/// <param name="Snippet">The optional snippet that matched the query.</param>
/// <param name="Score">The relevance score.</param>
/// <param name="LastModifiedUtc">The last modification timestamp.</param>
public sealed record SearchHitDto(
    Guid FileId,
    string Title,
    string Mime,
    string? Snippet,
    double Score,
    DateTimeOffset LastModifiedUtc);
