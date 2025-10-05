namespace Veriado.Infrastructure.Persistence;

/// <summary>
/// Provides helper methods to detect SQLite full-text search capabilities.
/// </summary>
internal static class SqliteFulltextSupportDetector
{
    public static void Detect(InfrastructureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            options.IsFulltextAvailable = false;
            options.FulltextAvailabilityError = "Infrastructure connection string has not been initialised.";
            SqliteFulltextSupport.Update(false, options.FulltextAvailabilityError);
            return;
        }

        // FTS5 support has been removed from the application runtime. Instead of probing for
        // module availability, mark the capability as unavailable while keeping the write-ahead
        // journal infrastructure intact.
        const string reason = "SQLite FTS5 support disabled.";
        options.IsFulltextAvailable = false;
        options.FulltextAvailabilityError = reason;
        SqliteFulltextSupport.Update(false, reason);
    }
}
