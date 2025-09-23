using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Veriado.Appl.Abstractions;

/// <summary>
/// Provides verification and repair utilities for the full-text search index.
/// </summary>
public interface IFulltextIntegrityService
{
    /// <summary>
    /// Verifies the full-text index and returns information about inconsistencies.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The integrity report.</returns>
    Task<IntegrityReport> VerifyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Repairs the full-text index based on the supplied options.
    /// </summary>
    /// <param name="reindexAll">When <see langword="true"/>, all files are reindexed regardless of the current state.</param>
    /// <param name="extractContent">Indicates whether binary content should be reprocessed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of index entries that were updated.</returns>
    Task<int> RepairAsync(bool reindexAll, bool extractContent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the result of an integrity verification operation.
/// </summary>
/// <param name="MissingFileIds">The identifiers of files missing from the index.</param>
/// <param name="OrphanIndexIds">The identifiers present in the index without corresponding files.</param>
public sealed record IntegrityReport(
    IReadOnlyCollection<Guid> MissingFileIds,
    IReadOnlyCollection<Guid> OrphanIndexIds)
{
    public int MissingCount => MissingFileIds.Count;

    public int OrphanCount => OrphanIndexIds.Count;
}
