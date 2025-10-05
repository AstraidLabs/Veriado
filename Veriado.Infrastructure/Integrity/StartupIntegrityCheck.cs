using Microsoft.Extensions.DependencyInjection;
using Veriado.Infrastructure.Search;

namespace Veriado.Infrastructure.Integrity;

/// <summary>
/// Provides helper methods to invoke integrity checks during application startup.
/// </summary>
internal static class StartupIntegrityCheck
{
    public static async Task EnsureConsistencyAsync(IServiceProvider provider, CancellationToken cancellationToken = default)
    {
        var options = provider.GetRequiredService<InfrastructureOptions>();
        options.RunIntegrityCheckOnStartup = true;
        options.RepairIntegrityAutomatically = true;
        if (!options.RunIntegrityCheckOnStartup)
        {
            return;
        }

        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("FulltextIntegrity");
        var integrity = provider.GetRequiredService<IFulltextIntegrityService>();
        var report = await integrity.VerifyAsync(cancellationToken).ConfigureAwait(false);
        if (report.MissingCount == 0 && report.OrphanCount == 0)
        {
            logger.LogInformation("Full-text index verified: no inconsistencies detected");
            return;
        }

        logger.LogWarning("Full-text index inconsistencies detected: {Missing} missing, {Orphans} orphans", report.MissingCount, report.OrphanCount);
        if (report.MissingFileIds.Count > 0)
        {
            logger.LogWarning("Missing search index entries for files: {MissingIds}", string.Join(", ", report.MissingFileIds));
        }

        if (report.OrphanIndexIds.Count > 0)
        {
            logger.LogWarning("Orphaned search rows without matching files: {OrphanIds}", string.Join(", ", report.OrphanIndexIds));
        }

        if (options.RepairIntegrityAutomatically)
        {
            var repaired = await integrity.RepairAsync(reindexAll: false, cancellationToken)
                .ConfigureAwait(false);
            logger.LogInformation("Full-text index repair completed ({Repaired} entries)", repaired);
        }
    }
}
