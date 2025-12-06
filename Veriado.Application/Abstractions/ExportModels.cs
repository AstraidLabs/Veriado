using System;
using Veriado.Contracts.Storage;

namespace Veriado.Appl.Abstractions;

public sealed record ExportRequest
{
    public string DestinationPath { get; init; } = string.Empty;
    public string? PackageName { get; init; }
        = null;
    public string? Description { get; init; }
        = null;
    public bool OverwriteExisting { get; init; }
        = false;
    public bool EncryptPayload { get; init; }
        = false;
    public string? Password { get; init; }
        = null;
    public bool SignPayload { get; init; }
        = false;
    public Guid? SourceInstanceId { get; init; }
        = null;
    public string? SourceInstanceName { get; init; }
        = null;

    public StorageExportMode ExportMode { get; init; }
        = StorageExportMode.LogicalPerFile;

    public bool IncludeFileHashes { get; init; } = true;
}
