using System.Collections.Generic;
using System.IO;

namespace Veriado.Domain.ValueObjects;

/// <summary>
/// Represents a normalized, relative path within the configured storage root.
/// </summary>
public sealed class RelativeFilePath : IEquatable<RelativeFilePath>
{
    private const int MaxLength = 2048;

    private RelativeFilePath(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the normalized relative path value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a <see cref="RelativeFilePath"/> from the provided raw path.
    /// </summary>
    /// <param name="value">The relative path within the storage root.</param>
    /// <returns>The created value object.</returns>
    /// <exception cref="ArgumentException">Thrown when the value is null, empty, rooted, or exceeds the maximum length.</exception>
    public static RelativeFilePath From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Relative path cannot be null or whitespace.", nameof(value));
        }

        var trimmed = value.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            throw new ArgumentException("Relative path must not be rooted.", nameof(value));
        }

        var segments = NormalizeSegments(trimmed);
        var normalized = string.Join('/', segments);

        if (normalized.Length == 0)
        {
            throw new ArgumentException("Relative path cannot be empty.", nameof(value));
        }

        if (normalized.Length > MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                normalized.Length,
                $"Relative path exceeds the maximum allowed length of {MaxLength} characters.");
        }

        return new RelativeFilePath(normalized);
    }

    private static IEnumerable<string> NormalizeSegments(string path)
    {
        var segments = path
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            if (segment == "." || segment == "..")
            {
                throw new ArgumentException("Relative path cannot contain navigation segments.", nameof(path));
            }

            yield return segment;
        }
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <inheritdoc />
    public bool Equals(RelativeFilePath? other)
        => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is RelativeFilePath other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
}
