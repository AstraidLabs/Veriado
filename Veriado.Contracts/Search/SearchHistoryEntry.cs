using System;

namespace Veriado.Contracts.Search;

/// <summary>
/// Represents a persisted search history entry exposed to the presentation layer.
/// </summary>
/// <param name="Id">The entry identifier.</param>
/// <param name="QueryText">The optional user supplied query text.</param>
/// <param name="MatchQuery">The generated FTS match clause.</param>
/// <param name="LastQueriedUtc">The timestamp of the most recent execution.</param>
/// <param name="Executions">The number of times the query was executed.</param>
/// <param name="LastTotalHits">The last observed hit count.</param>
/// <param name="IsFuzzy">Indicates whether the query used fuzzy matching.</param>
public sealed record SearchHistoryEntry(
    Guid Id,
    string? QueryText,
    string MatchQuery,
    DateTimeOffset LastQueriedUtc,
    int Executions,
    int? LastTotalHits,
    bool IsFuzzy);
