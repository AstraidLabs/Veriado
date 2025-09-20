using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Veriado.Infrastructure.Integrity;

/// <summary>
/// Provides verification and repair utilities for the full-text search index.
/// </summary>
internal interface IFulltextIntegrityService
{
    Task<IntegrityReport> VerifyAsync(CancellationToken cancellationToken = default);

    Task RepairAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the result of an integrity verification operation.
/// </summary>
internal sealed record IntegrityReport(
    IReadOnlyCollection<Guid> MissingFileIds,
    IReadOnlyCollection<Guid> OrphanIndexIds)
{
    public int MissingCount => MissingFileIds.Count;

    public int OrphanCount => OrphanIndexIds.Count;
}
