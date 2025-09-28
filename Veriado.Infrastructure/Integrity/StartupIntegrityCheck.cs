using Microsoft.Extensions.DependencyInjection;

namespace Veriado.Infrastructure.Integrity;

/// <summary>
/// Provides helper methods to invoke integrity checks during application startup.
/// </summary>
internal static class StartupIntegrityCheck
{
    public static async Task EnsureConsistencyAsync(IServiceProvider provider, CancellationToken cancellationToken = default)
    {
        var options = provider.GetRequiredService<InfrastructureOptions>();
        if (!options.RunIntegrityCheckOnStartup)
        {
            return;
        }

        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("FulltextIntegrity");
        if (!options.IsFulltextAvailable)
        {
            var reason = options.FulltextAvailabilityError ?? "SQLite FTS5 support is unavailable.";
            logger.LogWarning("Skipping full-text integrity check because FTS5 support is unavailable: {Reason}", reason);
            return;
        }

        var integrity = provider.GetRequiredService<IFulltextIntegrityService>();
        var report = await integrity.VerifyAsync(cancellationToken).ConfigureAwait(false);
        if (report.MissingCount == 0 && report.OrphanCount == 0)
        {
            logger.LogInformation("Full-text index verified: no inconsistencies detected");
            return;
        }

        logger.LogWarning("Full-text index inconsistencies detected: {Missing} missing, {Orphans} orphans", report.MissingCount, report.OrphanCount);
        if (options.RepairIntegrityAutomatically)
        {
            var repaired = await integrity.RepairAsync(reindexAll: false, cancellationToken)
                .ConfigureAwait(false);
            logger.LogInformation("Full-text index repair completed ({Repaired} entries)", repaired);
        }
    }
}
