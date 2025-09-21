namespace Veriado.Contracts.Diagnostics;

/// <summary>
/// Represents aggregate metrics about the search index.
/// </summary>
/// <param name="TotalDocuments">Total number of indexed documents.</param>
/// <param name="StaleDocuments">Documents pending reindexing.</param>
/// <param name="FtsVersion">The SQLite FTS5 version string.</param>
public sealed record IndexStatisticsDto(
    int TotalDocuments,
    int StaleDocuments,
    string? FtsVersion);
