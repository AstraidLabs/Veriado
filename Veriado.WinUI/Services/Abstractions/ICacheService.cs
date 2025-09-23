using System;

namespace Veriado.Services.Abstractions;

public interface ICacheService
{
    bool TryGetValue<T>(string key, out T? value);

    void Set<T>(string key, T value, TimeSpan timeToLive);

    void Remove(string key);

    void Clear();
}
