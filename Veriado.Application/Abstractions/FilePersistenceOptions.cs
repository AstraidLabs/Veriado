using System;

namespace Veriado.Appl.Abstractions;

/// <summary>
/// Represents configuration flags that influence how file aggregates are persisted.
/// </summary>
public readonly struct FilePersistenceOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FilePersistenceOptions"/> struct.
    /// </summary>
    public FilePersistenceOptions()
    {
    }

    /// <summary>
    /// Gets a value indicating whether the binary content should be passed through the text extractor.
    /// </summary>
    public bool ExtractContent { get; init; }

    /// <summary>
    /// Gets a value indicating whether the search coordinator may defer indexing to the outbox pipeline.
    /// </summary>
    public bool AllowDeferredIndexing { get; init; } = true;

    /// <summary>
    /// Gets the default persistence options.
    /// </summary>
    public static FilePersistenceOptions Default => new();
}
