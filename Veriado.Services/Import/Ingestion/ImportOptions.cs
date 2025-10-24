using System;

namespace Veriado.Services.Import.Ingestion;

/// <summary>
/// Provides configuration for streaming file ingestion.
/// </summary>
public sealed record class ImportOptions
{
    /// <summary>
    /// Gets or sets the buffer size used while copying content between streams.
    /// </summary>
    public int BufferSize { get; init; } = 128 * 1024;

    /// <summary>
    /// Gets or sets the maximum number of retries when the source file is temporarily unavailable.
    /// </summary>
    public int MaxRetryCount { get; init; } = 5;

    /// <summary>
    /// Gets or sets the base backoff delay applied between retries when opening the source file.
    /// </summary>
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Gets or sets the maximum backoff delay applied when retrying to open the source file.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets or sets the file sharing policy used when opening the source file.
    /// </summary>
    public FileOpenSharePolicy SharePolicy { get; init; } = FileOpenSharePolicy.ReadWrite;
}
