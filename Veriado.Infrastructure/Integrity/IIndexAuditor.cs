namespace Veriado.Infrastructure.Integrity;

public interface IIndexAuditor
{
    Task<AuditSummary> VerifyAsync(CancellationToken ct);
    Task<int> RepairDriftAsync(AuditSummary summary, CancellationToken ct);
}

public sealed record AuditSummary(List<string> Missing, List<string> Drift, List<string> Extra);
