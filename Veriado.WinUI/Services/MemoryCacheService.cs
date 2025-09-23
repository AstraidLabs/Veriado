using System;
using System.Collections.Concurrent;
using Veriado.Services.Abstractions;

namespace Veriado.Services;

public sealed class MemoryCacheService : ICacheService
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();
    private static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromMinutes(5);

    public bool TryGetValue<T>(string key, out T? value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (_entries.TryGetValue(key, out var entry) && !entry.IsExpired)
        {
            if (entry.Value is T typed)
            {
                value = typed;
                return true;
            }
        }
        else if (entry is not null && entry.IsExpired)
        {
            _entries.TryRemove(key, out _);
        }

        return false;
    }

    public void Set<T>(string key, T value, TimeSpan timeToLive)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (timeToLive <= TimeSpan.Zero)
        {
            timeToLive = DefaultTimeToLive;
        }

        var entry = new CacheEntry(value, DateTimeOffset.UtcNow.Add(timeToLive));
        _entries.AddOrUpdate(key, entry, (_, _) => entry);
    }

    public void Remove(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        _entries.TryRemove(key, out _);
    }

    public void Clear()
    {
        _entries.Clear();
    }

    private sealed record CacheEntry(object? Value, DateTimeOffset ExpiresAt)
    {
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    }
}
