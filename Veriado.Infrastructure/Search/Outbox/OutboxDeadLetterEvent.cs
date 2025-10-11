namespace Veriado.Infrastructure.Search.Outbox;

/// <summary>
/// Represents a failed outbox event that exhausted its retry budget.
/// </summary>
public sealed class OutboxDeadLetterEvent
{
    #region TODO(SQLiteOnly): Remove dead-letter persistence once deferred pipeline is retired
    public long Id { get; set; }
        = 0;

    public long OutboxId { get; set; }
        = 0;

    public string Type { get; set; }
        = string.Empty;

    public string Payload { get; set; }
        = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }
        = default;

    public DateTimeOffset DeadLetteredUtc { get; set; }
        = default;

    public int Attempts { get; set; }
        = 0;

    public string Error { get; set; }
        = string.Empty;
    #endregion
}
