using System;

namespace Veriado.Infrastructure.FileSystem;

/// <summary>
/// Configures the background health check of physical file system entities.
/// </summary>
public sealed class FileSystemHealthCheckOptions
{
    /// <summary>
    /// Gets or sets the interval between health check iterations.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the number of files processed per batch.
    /// </summary>
    public int BatchSize { get; set; } = 200;

    /// <summary>
    /// Gets or sets a value indicating whether file checks should run in parallel.
    /// </summary>
    public bool EnableParallelChecks { get; set; }

    /// <summary>
    /// Gets or sets the maximum degree of parallelism when parallel checks are enabled.
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 4;
}
