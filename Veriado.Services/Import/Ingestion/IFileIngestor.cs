using System;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Appl.Abstractions;

namespace Veriado.Services.Import.Ingestion;

/// <summary>
/// Provides streaming ingestion support for files imported from the local file system.
/// </summary>
public interface IFileIngestor
{
    /// <summary>
    /// Streams the source file into the configured storage provider.
    /// </summary>
    /// <param name="request">The ingestion request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ingestion result containing storage metadata.</returns>
    Task<FileIngestResult> IngestAsync(FileIngestRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Describes a file ingestion request.
/// </summary>
public sealed record class FileIngestRequest
{
    public FileIngestRequest(string sourcePath, string? preferredStoragePath, ImportOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);

        SourcePath = sourcePath;
        PreferredStoragePath = preferredStoragePath;
        Options = options ?? new ImportOptions();
    }

    /// <summary>
    /// Gets the absolute path to the source file that should be imported.
    /// </summary>
    public string SourcePath { get; }

    /// <summary>
    /// Gets the optional preferred storage path.
    /// </summary>
    public string? PreferredStoragePath { get; }

    /// <summary>
    /// Gets the ingestion options.
    /// </summary>
    public ImportOptions Options { get; }
}

/// <summary>
/// Describes the outcome of a file ingestion operation.
/// </summary>
/// <param name="Storage">The storage metadata snapshot.</param>
/// <param name="Sha1Hex">The optional SHA-1 hash computed for the content.</param>
public sealed record class FileIngestResult(StorageResult Storage, string? Sha1Hex);
