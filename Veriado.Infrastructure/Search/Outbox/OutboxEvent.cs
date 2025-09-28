using System.Text.Json;

namespace Veriado.Infrastructure.Search.Outbox;

/// <summary>
/// Represents an event persisted for deferred processing by the outbox worker.
/// </summary>
public sealed class OutboxEvent
{
    public long Id { get; set; }
        = 0;

    public string Type { get; set; }
        = string.Empty;

    public string Payload { get; set; }
        = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }
        = default;

    public DateTimeOffset? ProcessedUtc { get; set; }
        = null;

    public static OutboxEvent From(string type, object payload, DateTimeOffset createdUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        var json = JsonSerializer.Serialize(payload);
        return new OutboxEvent
        {
            Type = type,
            Payload = json,
            CreatedUtc = createdUtc.ToUniversalTime(),
        };
    }
}
