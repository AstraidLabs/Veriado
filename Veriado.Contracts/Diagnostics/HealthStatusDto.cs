namespace Veriado.Contracts.Diagnostics;

/// <summary>
/// Represents diagnostic information about the underlying SQLite database.
/// </summary>
/// <param name="DatabasePath">The absolute database path.</param>
/// <param name="JournalMode">The current journal mode.</param>
/// <param name="IsWalEnabled">Indicates whether WAL journaling is enabled.</param>
/// <param name="PendingOutboxEvents">Number of pending outbox events.</param>
public sealed record HealthStatusDto(
    string DatabasePath,
    string JournalMode,
    bool IsWalEnabled,
    int PendingOutboxEvents);
