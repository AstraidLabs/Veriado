namespace Veriado.Infrastructure.Persistence;

/// <summary>
/// Provides shared state describing the availability of SQLite full-text search features.
/// </summary>
internal static class SqliteFulltextSupport
{
    private static int _initialised;
    private static int _isAvailable;
    private static string? _failureReason;

    /// <summary>
    /// Gets a value indicating whether the required FTS5 features are available.
    /// </summary>
    public static bool IsAvailable => Volatile.Read(ref _initialised) == 1 && Volatile.Read(ref _isAvailable) == 1;

    /// <summary>
    /// Gets the last detected failure reason when FTS5 support is unavailable.
    /// </summary>
    public static string? FailureReason => Volatile.Read(ref _failureReason);

    /// <summary>
    /// Updates the cached state describing FTS5 support for the current process.
    /// </summary>
    /// <param name="available">Indicates whether FTS5 support is available.</param>
    /// <param name="failureReason">The failure reason, if any.</param>
    public static void Update(bool available, string? failureReason)
    {
        Volatile.Write(ref _isAvailable, available ? 1 : 0);
        Volatile.Write(ref _failureReason, failureReason);
        Volatile.Write(ref _initialised, 1);
    }
}
