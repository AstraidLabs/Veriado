using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Veriado.Domain.Metadata;

/// <summary>
/// Represents an immutable collection of extended metadata values indexed by property keys.
/// </summary>
public sealed class ExtendedMetadata : IEquatable<ExtendedMetadata>
{
    private readonly IReadOnlyDictionary<PropertyKey, MetadataValue> _values;

    private ExtendedMetadata(IDictionary<PropertyKey, MetadataValue> values)
    {
        _values = new ReadOnlyDictionary<PropertyKey, MetadataValue>(new Dictionary<PropertyKey, MetadataValue>(values));
    }

    private ExtendedMetadata(IReadOnlyDictionary<PropertyKey, MetadataValue> values)
    {
        _values = values;
    }

    /// <summary>
    /// Gets an empty metadata collection.
    /// </summary>
    public static ExtendedMetadata Empty { get; } =
        new((IDictionary<PropertyKey, MetadataValue>)new Dictionary<PropertyKey, MetadataValue>());

    /// <summary>
    /// Creates a new builder initialized with the current metadata values.
    /// </summary>
    /// <returns>The builder instance.</returns>
    public Builder ToBuilder() => new(_values);

    /// <summary>
    /// Attempts to retrieve a metadata value for the specified key.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <param name="value">The retrieved metadata value.</param>
    /// <returns><see langword="true"/> if a value was found; otherwise <see langword="false"/>.</returns>
    public bool TryGet(PropertyKey key, out MetadataValue value) => _values.TryGetValue(key, out value);

    /// <summary>
    /// Returns the metadata values as an enumeration of key/value pairs.
    /// </summary>
    /// <returns>The enumerable sequence of metadata values.</returns>
    public IEnumerable<KeyValuePair<PropertyKey, MetadataValue>> AsEnumerable() => _values.ToArray();

    /// <inheritdoc />
    public bool Equals(ExtendedMetadata? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (_values.Count != other._values.Count)
        {
            return false;
        }

        foreach (var pair in _values)
        {
            if (!other._values.TryGetValue(pair.Key, out var otherValue))
            {
                return false;
            }

            if (!pair.Value.Equals(otherValue))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ExtendedMetadata other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var pair in _values.OrderBy(p => p.Key.FormatId).ThenBy(p => p.Key.PropertyId))
        {
            hash.Add(pair.Key);
            hash.Add(pair.Value);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Provides functionality to construct and mutate metadata values before committing changes.
    /// </summary>
    public sealed class Builder
    {
        private readonly Dictionary<PropertyKey, MetadataValue> _mutable;

        internal Builder(IEnumerable<KeyValuePair<PropertyKey, MetadataValue>> values)
        {
            _mutable = values.ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        /// <summary>
        /// Sets the metadata value for the specified property key.
        /// </summary>
        /// <param name="key">The property key.</param>
        /// <param name="value">The metadata value to store.</param>
        /// <returns>The same builder instance for chaining.</returns>
        public Builder Set(PropertyKey key, MetadataValue value)
        {
            _mutable[key] = value;
            return this;
        }

        /// <summary>
        /// Removes a metadata value for the specified key if it exists.
        /// </summary>
        /// <param name="key">The property key to remove.</param>
        /// <returns>The same builder instance for chaining.</returns>
        public Builder Remove(PropertyKey key)
        {
            _mutable.Remove(key);
            return this;
        }

        /// <summary>
        /// Builds an immutable <see cref="ExtendedMetadata"/> instance representing the current state of the builder.
        /// </summary>
        /// <returns>The immutable metadata collection.</returns>
        public ExtendedMetadata Build() =>
            new((IDictionary<PropertyKey, MetadataValue>)_mutable);
    }
}
