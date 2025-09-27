using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;

namespace Veriado.Infrastructure.Persistence.Migrations;

/// <summary>
/// Provides a more lenient implementation of <see cref="IMigrationsIdGenerator"/> that tolerates
/// duplicate identifiers by deriving alternative identifiers when collisions are detected.
/// </summary>
public sealed class LenientMigrationsIdGenerator : IMigrationsIdGenerator
{
    private const int TimestampLength = 14;

    private readonly MigrationsIdGenerator _inner = new();
    private readonly object _lock = new();
    private readonly HashSet<string> _generatedIds = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public string GenerateId(string name)
    {
        lock (_lock)
        {
            var candidate = _inner.GenerateId(name);
            candidate = EnsureUniqueInternal(candidate, id => !_generatedIds.Contains(id));
            _generatedIds.Add(candidate);

            return candidate;
        }
    }

    /// <inheritdoc />
    public bool IsValidId(string value)
        => _inner.IsValidId(value);

    /// <inheritdoc />
    public string GetName(string id)
        => _inner.GetName(id);

    internal string EnsureUniqueId(string preferredId, Func<string, bool> isAvailable)
    {
        lock (_lock)
        {
            var candidate = EnsureUniqueInternal(preferredId, id => isAvailable(id) && !_generatedIds.Contains(id));
            _generatedIds.Add(candidate);
            return candidate;
        }
    }

    private string EnsureUniqueInternal(string preferredId, Func<string, bool> isAvailable)
    {
        if (isAvailable(preferredId))
        {
            return preferredId;
        }

        var baseName = _inner.IsValidId(preferredId)
            ? _inner.GetName(preferredId)
            : preferredId;
        var timestampPrefix = TryExtractTimestampPrefix(preferredId);
        var attempt = 2;

        while (true)
        {
            var candidateName = $"{baseName}_{attempt++}";
            var candidateId = timestampPrefix is null
                ? _inner.GenerateId(candidateName)
                : $"{timestampPrefix}_{candidateName}";

            if (!isAvailable(candidateId))
            {
                continue;
            }

            return candidateId;
        }
    }

    private static string? TryExtractTimestampPrefix(string id)
    {
        if (id.Length <= TimestampLength || id[TimestampLength] != '_')
        {
            return null;
        }

        for (var i = 0; i < TimestampLength; i++)
        {
            if (!char.IsDigit(id[i]))
            {
                return null;
            }
        }

        return id[..TimestampLength];
    }
}
